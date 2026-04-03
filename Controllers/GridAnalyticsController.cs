using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SignalTracker.Models;
using SignalTracker.Services;

namespace SignalTracker.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class GridAnalyticsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly RedisService _redis;
        private readonly UserScopeService _userScope;
        private const double METERS_PER_DEGREE_LAT = 111320.0;

        public GridAnalyticsController(
            ApplicationDbContext context,
            RedisService redis,
            UserScopeService userScope)
        {
            _db = context;
            _redis = redis;
            _userScope = userScope;
        }

        // =====================================================================
        // POST api/GridAnalytics/ComputeAndStoreGridAnalytics
        // Evaluates the grid and stores results in the grid_analytics_results table
        // =====================================================================
        [HttpPost("ComputeAndStoreGridAnalytics")]
        public async Task<IActionResult> ComputeAndStoreGridAnalytics(
            [FromQuery] int projectId,
            [FromQuery] double? gridSize = null,
            [FromQuery] int? regionId = null,
            [FromQuery] int? company_id = null)
        {
            var sw = Stopwatch.StartNew();

            // ── 1. AUTH & COMPANY SCOPING ──
            int targetCompanyId = _userScope.GetTargetCompanyId(User, company_id);
            bool isSuperAdmin = _userScope.IsSuperAdmin(User);
            if (!isSuperAdmin && targetCompanyId == 0)
                return Unauthorized(new { Status = 0, Message = "Unauthorized. Unable to resolve company context." });

            try
            {
                var conn = _db.Database.GetDbConnection();
                bool shouldClose = false;

                if (conn.State != ConnectionState.Open)
                {
                    await conn.OpenAsync();
                    shouldClose = true;
                }

                try
                {
                    // ── ENSURE TABLE EXISTS ──
                    await using (var cmdCreate = conn.CreateCommand())
                    {
                        cmdCreate.CommandText = @"
                        CREATE TABLE IF NOT EXISTS grid_analytics_results (
                            id INT AUTO_INCREMENT PRIMARY KEY,
                            project_id INT NOT NULL,
                            region_id INT,
                            grid_size_meters DOUBLE NOT NULL,
                            grid_id VARCHAR(50) NOT NULL,
                            center_lat DOUBLE NOT NULL,
                            center_lon DOUBLE NOT NULL,
                            min_lat DOUBLE NOT NULL,
                            max_lat DOUBLE NOT NULL,
                            min_lon DOUBLE NOT NULL,
                            max_lon DOUBLE NOT NULL,
                            baseline_point_count INT NOT NULL,
                            optimized_point_count INT NOT NULL,

                            baseline_avg_rsrp DOUBLE, baseline_avg_rsrq DOUBLE, baseline_avg_sinr DOUBLE,
                            baseline_median_rsrp DOUBLE, baseline_median_rsrq DOUBLE, baseline_median_sinr DOUBLE,
                            baseline_max_rsrp DOUBLE, baseline_max_rsrq DOUBLE, baseline_max_sinr DOUBLE,
                            baseline_mode_rsrp DOUBLE, baseline_mode_rsrq DOUBLE, baseline_mode_sinr DOUBLE,

                            optimized_avg_rsrp DOUBLE, optimized_avg_rsrq DOUBLE, optimized_avg_sinr DOUBLE,
                            optimized_median_rsrp DOUBLE, optimized_median_rsrq DOUBLE, optimized_median_sinr DOUBLE,
                            optimized_max_rsrp DOUBLE, optimized_max_rsrq DOUBLE, optimized_max_sinr DOUBLE,
                            optimized_mode_rsrp DOUBLE, optimized_mode_rsrq DOUBLE, optimized_mode_sinr DOUBLE,

                            diff_avg_rsrp DOUBLE, diff_avg_rsrq DOUBLE, diff_avg_sinr DOUBLE,
                            diff_median_rsrp DOUBLE, diff_median_rsrq DOUBLE, diff_median_sinr DOUBLE,
                            diff_max_rsrp DOUBLE, diff_max_rsrq DOUBLE, diff_max_sinr DOUBLE,
                            diff_mode_rsrp DOUBLE, diff_mode_rsrq DOUBLE, diff_mode_sinr DOUBLE,

                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        );";
                        await cmdCreate.ExecuteNonQueryAsync();
                    }

                    // ── 3. FETCH grid_size FROM tbl_project ──
                    double gridSizeMeters = gridSize ?? 0;
                    if (gridSizeMeters <= 0)
                    {
                        await using var cmdProj = conn.CreateCommand();
                        cmdProj.CommandText = "SELECT grid_size FROM tbl_project WHERE id = @pid";
                        AddParam(cmdProj, "@pid", projectId);
                        var gsRaw = await cmdProj.ExecuteScalarAsync();
                        if (gsRaw != null && gsRaw != DBNull.Value)
                            double.TryParse(gsRaw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out gridSizeMeters);
                    }
                    if (gridSizeMeters <= 0)
                        return BadRequest(new { Status = 0, Message = "grid_size not available. Pass gridSize query param (meters)." });

                    // ── 4. SECURITY: project belongs to company ──
                    if (targetCompanyId > 0)
                    {
                        await using var cmdAcc = conn.CreateCommand();
                        cmdAcc.CommandText = "SELECT COUNT(1) FROM tbl_project WHERE id = @pid AND company_id = @cid";
                        AddParam(cmdAcc, "@pid", projectId);
                        AddParam(cmdAcc, "@cid", targetCompanyId);
                        var accRes = await cmdAcc.ExecuteScalarAsync();
                        if (accRes == null || Convert.ToInt32(accRes) == 0)
                            return Unauthorized(new { Status = 0, Message = "Project does not belong to your company." });
                    }

                    // ── 5. FETCH POLYGON WKT from map_regions ──
                    string? polygonWkt = null;
                    await using var cmdPoly = conn.CreateCommand();
                    if (regionId.HasValue && regionId.Value > 0)
                    {
                        cmdPoly.CommandText = "SELECT ST_AsText(region) FROM map_regions WHERE id = @rid AND tbl_project_id = @pid AND status = 1 LIMIT 1";
                        AddParam(cmdPoly, "@rid", regionId.Value);
                        AddParam(cmdPoly, "@pid", projectId);
                    }
                    else
                    {
                        cmdPoly.CommandText = "SELECT ST_AsText(region) FROM map_regions WHERE tbl_project_id = @pid AND status = 1 LIMIT 1";
                        AddParam(cmdPoly, "@pid", projectId);
                    }
                    var polyRes = await cmdPoly.ExecuteScalarAsync();
                    if (polyRes != null && polyRes != DBNull.Value)
                        polygonWkt = polyRes.ToString();
                    if (string.IsNullOrWhiteSpace(polygonWkt))
                        return BadRequest(new { Status = 0, Message = "No polygon found for this project." });

                    // ── 6. PARSE POLYGON & GENERATE GRID ──
                    var vertices = ParsePolygonWkt(polygonWkt);
                    if (vertices.Count < 3)
                        return BadRequest(new { Status = 0, Message = "Invalid polygon geometry." });

                    var (gridCells, gLat, gLon, mLat, mLon) = GenerateGrid(vertices, gridSizeMeters);
                    if (gridCells.Count == 0)
                        return Ok(new GridAnalyticsResponse { Status = 1, Message = "No grid cells inside polygon." });

                    // ── 7. FETCH PREDICTION DATA (raw ADO.NET) ──
                    var baselinePts = await FetchPredictionData(conn, "lte_prediction_baseline_results", projectId);
                    var optimizedPts = await FetchPredictionData(conn, "lte_prediction_optimised_results", projectId);

                    // ── 8. MAP POINTS → GRIDS ──
                    var baseByGrid = MapPointsToGrids(baselinePts, mLat, mLon, gLat, gLon, gridCells);
                    var optByGrid = MapPointsToGrids(optimizedPts, mLat, mLon, gLat, gLon, gridCells);

                    // ── 9. COMPUTE METRICS & DIFFERENCES ──
                    var resultsList = new List<grid_analytics_results>();
                    foreach (var cell in gridCells.Values)
                    {
                        var bData = baseByGrid.TryGetValue(cell.Key, out var bl) ? bl : new List<PredPoint>();
                        var oData = optByGrid.TryGetValue(cell.Key, out var ol) ? ol : new List<PredPoint>();
                        if (bData.Count == 0 && oData.Count == 0) continue;

                        var bm = ComputeMetrics(bData);
                        var om = ComputeMetrics(oData);
                        var diff = ComputeDiff(bm, om);

                        resultsList.Add(new grid_analytics_results
                        {
                            project_id = projectId,
                            region_id = regionId,
                            grid_size_meters = gridSizeMeters,
                            grid_id = cell.GridId,
                            center_lat = cell.CenterLat, center_lon = cell.CenterLon,
                            min_lat = cell.MinLat, min_lon = cell.MinLon,
                            max_lat = cell.MaxLat, max_lon = cell.MaxLon,

                            baseline_point_count = bData.Count,
                            optimized_point_count = oData.Count,

                            baseline_avg_rsrp = bm.avg_rsrp, baseline_avg_rsrq = bm.avg_rsrq, baseline_avg_sinr = bm.avg_sinr,
                            baseline_median_rsrp = bm.median_rsrp, baseline_median_rsrq = bm.median_rsrq, baseline_median_sinr = bm.median_sinr,
                            baseline_max_rsrp = bm.max_rsrp, baseline_max_rsrq = bm.max_rsrq, baseline_max_sinr = bm.max_sinr,
                            baseline_mode_rsrp = bm.mode_rsrp, baseline_mode_rsrq = bm.mode_rsrq, baseline_mode_sinr = bm.mode_sinr,

                            optimized_avg_rsrp = om.avg_rsrp, optimized_avg_rsrq = om.avg_rsrq, optimized_avg_sinr = om.avg_sinr,
                            optimized_median_rsrp = om.median_rsrp, optimized_median_rsrq = om.median_rsrq, optimized_median_sinr = om.median_sinr,
                            optimized_max_rsrp = om.max_rsrp, optimized_max_rsrq = om.max_rsrq, optimized_max_sinr = om.max_sinr,
                            optimized_mode_rsrp = om.mode_rsrp, optimized_mode_rsrq = om.mode_rsrq, optimized_mode_sinr = om.mode_sinr,

                            diff_avg_rsrp = diff.diff_avg_rsrp, diff_avg_rsrq = diff.diff_avg_rsrq, diff_avg_sinr = diff.diff_avg_sinr,
                            diff_median_rsrp = diff.diff_median_rsrp, diff_median_rsrq = diff.diff_median_rsrq, diff_median_sinr = diff.diff_median_sinr,
                            diff_max_rsrp = diff.diff_max_rsrp, diff_max_rsrq = diff.diff_max_rsrq, diff_max_sinr = diff.diff_max_sinr,
                            diff_mode_rsrp = diff.diff_mode_rsrp, diff_mode_rsrq = diff.diff_mode_rsrq, diff_mode_sinr = diff.diff_mode_sinr,
                            created_at = DateTime.UtcNow
                        });
                    }

                    // ── 10. REMOVE EXISTING AND STORE TO DATABASE ──
                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        await using (var cmdDel = conn.CreateCommand())
                        {
                            cmdDel.Transaction = transaction;
                            if (regionId.HasValue && regionId.Value > 0)
                            {
                                cmdDel.CommandText = "DELETE FROM grid_analytics_results WHERE project_id = @pid AND region_id = @rid";
                                AddParam(cmdDel, "@rid", regionId.Value);
                                AddParam(cmdDel, "@pid", projectId);
                            }
                            else
                            {
                                cmdDel.CommandText = "DELETE FROM grid_analytics_results WHERE project_id = @pid AND (region_id IS NULL OR region_id <= 0)";
                                AddParam(cmdDel, "@pid", projectId);
                            }
                            await cmdDel.ExecuteNonQueryAsync();
                        }
                        
                        if (resultsList.Any())
                        {
                            _db.grid_analytics_results.AddRange(resultsList);
                            await _db.SaveChangesAsync();
                        }

                        await transaction.CommitAsync();
                    }

                    // Invalidate potentially cached read calls
                    string cacheKey = $"gridanalytics:{projectId}:{regionId ?? 0}";
                    if (_redis != null && _redis.IsConnected)
                    {
                        try { await _redis.DeleteAsync(cacheKey); } catch { }
                    }

                    sw.Stop();
                    var response = new GridAnalyticsResponse
                    {
                        Status = 1,
                        Message = $"Grid analytics computed and STORED successfully. {resultsList.Count} grids with data saved.",
                        Data = new GridAnalyticsData
                        {
                            project_id = projectId, grid_size_meters = gridSizeMeters,
                            total_grids = gridCells.Count, total_grids_with_data = resultsList.Count,
                            total_baseline_points = baselinePts.Count, total_optimized_points = optimizedPts.Count,
                            grids = ConvertToGridCellResults(resultsList)
                        }
                    };
                    return Ok(response);
                }
                finally
                {
                    if (shouldClose && conn.State == ConnectionState.Open)
                        await conn.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                return StatusCode(500, new { Status = 0, Message = "Error: " + ex.Message, StackTrace = ex.StackTrace });
            }
        }

        // =====================================================================
        // GET api/GridAnalytics/GetGridAnalytics
        // Fetches stored grid analytics for a project from the DB
        // =====================================================================
        [HttpGet("GetGridAnalytics")]
        public async Task<IActionResult> GetGridAnalytics(
            [FromQuery] int projectId,
            [FromQuery] int? regionId = null,
            [FromQuery] int? company_id = null)
        {
            var sw = Stopwatch.StartNew();

            // Auth & Scoping
            int targetCompanyId = _userScope.GetTargetCompanyId(User, company_id);
            bool isSuperAdmin = _userScope.IsSuperAdmin(User);
            if (!isSuperAdmin && targetCompanyId == 0)
                return Unauthorized(new { Status = 0, Message = "Unauthorized. Unable to resolve company context." });

            try
            {
                // Security check
                if (targetCompanyId > 0)
                {
                    bool access = await _db.tbl_project.AnyAsync(p => p.id == projectId && p.company_id == targetCompanyId);
                    if (!access)
                        return Unauthorized(new { Status = 0, Message = "Project does not belong to your company." });
                }

                string cacheKey = $"gridanalytics:{projectId}:{regionId ?? 0}";
                if (_redis != null && _redis.IsConnected)
                {
                    try
                    {
                        var cached = await _redis.GetObjectAsync<GridAnalyticsResponse>(cacheKey);
                        if (cached != null)
                        {
                            sw.Stop();
                            Response.Headers["X-Cache"] = "HIT";
                            return Ok(cached);
                        }
                    }
                    catch { }
                }

                // Check Table Exists
                bool tableExists = false;
                var conn = _db.Database.GetDbConnection();
                await conn.OpenAsync();
                await using (var cmdSchema = conn.CreateCommand())
                {
                    cmdSchema.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'grid_analytics_results';";
                    var count = Convert.ToInt32(await cmdSchema.ExecuteScalarAsync());
                    tableExists = count > 0;
                }
                
                if(!tableExists) {
                    await conn.CloseAsync();
                    return Ok(new GridAnalyticsResponse { Status = 0, Message = "Table does not exist. Call ComputeAndStoreGridAnalytics first." });
                }

                // Fetch directly from DB using EF
                List<grid_analytics_results> storedResults;
                if (regionId.HasValue && regionId.Value > 0)
                {
                    storedResults = await _db.grid_analytics_results
                        .Where(g => g.project_id == projectId && g.region_id == regionId)
                        .ToListAsync();
                }
                else
                {
                    storedResults = await _db.grid_analytics_results
                        .Where(g => g.project_id == projectId && (g.region_id == null || g.region_id <= 0))
                        .ToListAsync();
                }
                
                await conn.CloseAsync();

                if (storedResults.Count == 0)
                {
                    return Ok(new GridAnalyticsResponse
                    {
                        Status = 1,
                        Message = "No stored grid analytics found for this project. Please call ComputeAndStoreGridAnalytics first.",
                        Data = null
                    });
                }

                var responseData = new GridAnalyticsData
                {
                    project_id = projectId,
                    grid_size_meters = storedResults.First().grid_size_meters,
                    total_grids_with_data = storedResults.Count,
                    total_baseline_points = storedResults.Sum(s => s.baseline_point_count),
                    total_optimized_points = storedResults.Sum(s => s.optimized_point_count),
                    grids = ConvertToGridCellResults(storedResults)
                };

                var response = new GridAnalyticsResponse
                {
                    Status = 1,
                    Message = "Grid analytics fetched successfully from storage.",
                    Data = responseData
                };

                if (_redis != null && _redis.IsConnected)
                {
                    try { await _redis.SetObjectAsync(cacheKey, response, ttlSeconds: 600); } catch { }
                }

                sw.Stop();
                Response.Headers["X-Cache"] = "MISS";
                Response.Headers["X-Total-Ms"] = sw.ElapsedMilliseconds.ToString();
                return Ok(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return StatusCode(500, new { Status = 0, Message = "Error: " + ex.Message, StackTrace = ex.StackTrace });
            }
        }


        // =====================================================================
        // HELPERS
        // =====================================================================
        private static List<(double Lat, double Lon)> ParsePolygonWkt(string wkt)
        {
            var pts = new List<(double Lat, double Lon)>();
            var m = Regex.Match(wkt, @"\(\((.+?)\)\)", RegexOptions.Singleline);
            if (!m.Success) return pts;
            foreach (var pair in m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = pair.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 2
                    && double.TryParse(p[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double lon)
                    && double.TryParse(p[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double lat))
                    pts.Add((lat, lon));
            }
            return pts;
        }

        private static (Dictionary<string, GridCell> cells, double gLat, double gLon, double minLat, double minLon)
            GenerateGrid(List<(double Lat, double Lon)> poly, double sizeMeters)
        {
            double minLat = poly.Min(p => p.Lat), maxLat = poly.Max(p => p.Lat);
            double minLon = poly.Min(p => p.Lon), maxLon = poly.Max(p => p.Lon);
            double centerLat = (minLat + maxLat) / 2.0;

            double gLat = sizeMeters / METERS_PER_DEGREE_LAT;
            double gLon = sizeMeters / (METERS_PER_DEGREE_LAT * Math.Cos(centerLat * Math.PI / 180.0));

            var cells = new Dictionary<string, GridCell>();
            int rows = (int)Math.Ceiling((maxLat - minLat) / gLat);
            int cols = (int)Math.Ceiling((maxLon - minLon) / gLon);

            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                double cMinLat = minLat + r * gLat, cMaxLat = cMinLat + gLat;
                double cMinLon = minLon + c * gLon, cMaxLon = cMinLon + gLon;
                double cLat = (cMinLat + cMaxLat) / 2.0, cLon = (cMinLon + cMaxLon) / 2.0;

                if (PointInPolygon(cLat, cLon, poly))
                {
                    string key = $"R{r}C{c}";
                    cells[key] = new GridCell
                    {
                        Key = key, GridId = key, Row = r, Col = c,
                        MinLat = Math.Round(cMinLat, 8), MaxLat = Math.Round(cMaxLat, 8),
                        MinLon = Math.Round(cMinLon, 8), MaxLon = Math.Round(cMaxLon, 8),
                        CenterLat = Math.Round(cLat, 8), CenterLon = Math.Round(cLon, 8)
                    };
                }
            }
            return (cells, gLat, gLon, minLat, minLon);
        }

        private static bool PointInPolygon(double lat, double lon, List<(double Lat, double Lon)> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                double yi = poly[i].Lat, xi = poly[i].Lon;
                double yj = poly[j].Lat, xj = poly[j].Lon;
                if (((yi > lat) != (yj > lat)) && (lon < (xj - xi) * (lat - yi) / (yj - yi) + xi))
                    inside = !inside;
            }
            return inside;
        }

        private async Task<List<PredPoint>> FetchPredictionData(DbConnection conn, string table, int projectId)
        {
            var pts = new List<PredPoint>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT lat, lon, pred_rsrp, pred_rsrq, pred_sinr FROM `{table}` WHERE project_id = @pid";
            AddParam(cmd, "@pid", projectId);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                pts.Add(new PredPoint
                {
                    Lat = rdr.IsDBNull(0) ? 0 : rdr.GetDouble(0),
                    Lon = rdr.IsDBNull(1) ? 0 : rdr.GetDouble(1),
                    Rsrp = rdr.IsDBNull(2) ? null : Convert.ToDouble(rdr.GetValue(2)),
                    Rsrq = rdr.IsDBNull(3) ? null : Convert.ToDouble(rdr.GetValue(3)),
                    Sinr = rdr.IsDBNull(4) ? null : Convert.ToDouble(rdr.GetValue(4))
                });
            }
            return pts;
        }

        private static Dictionary<string, List<PredPoint>> MapPointsToGrids(
            List<PredPoint> pts, double minLat, double minLon,
            double gLat, double gLon, Dictionary<string, GridCell> valid)
        {
            var dict = new Dictionary<string, List<PredPoint>>();
            foreach (var pt in pts)
            {
                int row = (int)((pt.Lat - minLat) / gLat);
                int col = (int)((pt.Lon - minLon) / gLon);
                string key = $"R{row}C{col}";
                if (!valid.ContainsKey(key)) continue;
                if (!dict.ContainsKey(key)) dict[key] = new List<PredPoint>();
                dict[key].Add(pt);
            }
            return dict;
        }

        private static GridMetrics ComputeMetrics(List<PredPoint> pts)
        {
            if (pts == null || pts.Count == 0) return new GridMetrics { point_count = 0 };
            var rp = pts.Where(p => p.Rsrp.HasValue).Select(p => p.Rsrp!.Value).ToList();
            var rq = pts.Where(p => p.Rsrq.HasValue).Select(p => p.Rsrq!.Value).ToList();
            var sn = pts.Where(p => p.Sinr.HasValue).Select(p => p.Sinr!.Value).ToList();
            return new GridMetrics
            {
                point_count = pts.Count,
                avg_rsrp = Avg(rp), avg_rsrq = Avg(rq), avg_sinr = Avg(sn),
                median_rsrp = Median(rp), median_rsrq = Median(rq), median_sinr = Median(sn),
                max_rsrp = Max(rp), max_rsrq = Max(rq), max_sinr = Max(sn),
                mode_rsrp = Mode(rp), mode_rsrq = Mode(rq), mode_sinr = Mode(sn)
            };
        }

        private static GridDifference ComputeDiff(GridMetrics b, GridMetrics o)
        {
            return new GridDifference
            {
                diff_avg_rsrp = D(o.avg_rsrp, b.avg_rsrp), diff_avg_rsrq = D(o.avg_rsrq, b.avg_rsrq), diff_avg_sinr = D(o.avg_sinr, b.avg_sinr),
                diff_median_rsrp = D(o.median_rsrp, b.median_rsrp), diff_median_rsrq = D(o.median_rsrq, b.median_rsrq), diff_median_sinr = D(o.median_sinr, b.median_sinr),
                diff_max_rsrp = D(o.max_rsrp, b.max_rsrp), diff_max_rsrq = D(o.max_rsrq, b.max_rsrq), diff_max_sinr = D(o.max_sinr, b.max_sinr),
                diff_mode_rsrp = D(o.mode_rsrp, b.mode_rsrp), diff_mode_rsrq = D(o.mode_rsrq, b.mode_rsrq), diff_mode_sinr = D(o.mode_sinr, b.mode_sinr)
            };
        }

        private static List<GridCellResult> ConvertToGridCellResults(List<grid_analytics_results> stored)
        {
            var res = new List<GridCellResult>();
            foreach (var s in stored)
            {
                res.Add(new GridCellResult
                {
                    grid_id = s.grid_id,
                    center_lat = s.center_lat,
                    center_lon = s.center_lon,
                    min_lat = s.min_lat,
                    max_lat = s.max_lat,
                    min_lon = s.min_lon,
                    max_lon = s.max_lon,
                    baseline = new GridMetrics
                    {
                        point_count = s.baseline_point_count,
                        avg_rsrp = s.baseline_avg_rsrp, avg_rsrq = s.baseline_avg_rsrq, avg_sinr = s.baseline_avg_sinr,
                        median_rsrp = s.baseline_median_rsrp, median_rsrq = s.baseline_median_rsrq, median_sinr = s.baseline_median_sinr,
                        max_rsrp = s.baseline_max_rsrp, max_rsrq = s.baseline_max_rsrq, max_sinr = s.baseline_max_sinr,
                        mode_rsrp = s.baseline_mode_rsrp, mode_rsrq = s.baseline_mode_rsrq, mode_sinr = s.baseline_mode_sinr,
                    },
                    optimized = new GridMetrics
                    {
                        point_count = s.optimized_point_count,
                        avg_rsrp = s.optimized_avg_rsrp, avg_rsrq = s.optimized_avg_rsrq, avg_sinr = s.optimized_avg_sinr,
                        median_rsrp = s.optimized_median_rsrp, median_rsrq = s.optimized_median_rsrq, median_sinr = s.optimized_median_sinr,
                        max_rsrp = s.optimized_max_rsrp, max_rsrq = s.optimized_max_rsrq, max_sinr = s.optimized_max_sinr,
                        mode_rsrp = s.optimized_mode_rsrp, mode_rsrq = s.optimized_mode_rsrq, mode_sinr = s.optimized_mode_sinr,
                    },
                    difference = new GridDifference
                    {
                        diff_avg_rsrp = s.diff_avg_rsrp, diff_avg_rsrq = s.diff_avg_rsrq, diff_avg_sinr = s.diff_avg_sinr,
                        diff_median_rsrp = s.diff_median_rsrp, diff_median_rsrq = s.diff_median_rsrq, diff_median_sinr = s.diff_median_sinr,
                        diff_max_rsrp = s.diff_max_rsrp, diff_max_rsrq = s.diff_max_rsrq, diff_max_sinr = s.diff_max_sinr,
                        diff_mode_rsrp = s.diff_mode_rsrp, diff_mode_rsrq = s.diff_mode_rsrq, diff_mode_sinr = s.diff_mode_sinr,
                    }
                });
            }
            return res;
        }

        private static double? Avg(List<double> v) => v.Count > 0 ? Math.Round(v.Average(), 2) : null;
        private static double? Max(List<double> v) => v.Count > 0 ? Math.Round(v.Max(), 2) : null;
        private static double? D(double? a, double? b) => (a.HasValue && b.HasValue) ? Math.Round(a.Value - b.Value, 2) : null;

        private static double? Median(List<double> v)
        {
            if (v.Count == 0) return null;
            var s = v.OrderBy(x => x).ToList();
            int n = s.Count;
            return n % 2 == 0 ? Math.Round((s[n / 2 - 1] + s[n / 2]) / 2.0, 2) : Math.Round(s[n / 2], 2);
        }

        private static double? Mode(List<double> v)
        {
            if (v.Count == 0) return null;
            return v.GroupBy(x => Math.Round(x, 0))
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                    .First().Key;
        }

        private static void AddParam(DbCommand cmd, string name, object? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        // =====================================================================
        // DTOs
        // =====================================================================
        private class PredPoint
        {
            public double Lat { get; set; }
            public double Lon { get; set; }
            public double? Rsrp { get; set; }
            public double? Rsrq { get; set; }
            public double? Sinr { get; set; }
        }

        private class GridCell
        {
            public string Key { get; set; } = "";
            public string GridId { get; set; } = "";
            public int Row { get; set; }
            public int Col { get; set; }
            public double MinLat { get; set; }
            public double MaxLat { get; set; }
            public double MinLon { get; set; }
            public double MaxLon { get; set; }
            public double CenterLat { get; set; }
            public double CenterLon { get; set; }
        }

        public class GridAnalyticsResponse
        {
            public int Status { get; set; }
            public string Message { get; set; } = "";
            public GridAnalyticsData? Data { get; set; }
        }

        public class GridAnalyticsData
        {
            public int project_id { get; set; }
            public double grid_size_meters { get; set; }
            public int total_grids { get; set; }
            public int total_grids_with_data { get; set; }
            public int total_baseline_points { get; set; }
            public int total_optimized_points { get; set; }
            public List<GridCellResult> grids { get; set; } = new();
        }

        public class GridCellResult
        {
            public string grid_id { get; set; } = "";
            public double center_lat { get; set; }
            public double center_lon { get; set; }
            public double min_lat { get; set; }
            public double min_lon { get; set; }
            public double max_lat { get; set; }
            public double max_lon { get; set; }
            public GridMetrics baseline { get; set; } = new();
            public GridMetrics optimized { get; set; } = new();
            public GridDifference difference { get; set; } = new();
        }

        public class GridMetrics
        {
            public int point_count { get; set; }
            public double? avg_rsrp { get; set; }
            public double? avg_rsrq { get; set; }
            public double? avg_sinr { get; set; }
            public double? median_rsrp { get; set; }
            public double? median_rsrq { get; set; }
            public double? median_sinr { get; set; }
            public double? max_rsrp { get; set; }
            public double? max_rsrq { get; set; }
            public double? max_sinr { get; set; }
            public double? mode_rsrp { get; set; }
            public double? mode_rsrq { get; set; }
            public double? mode_sinr { get; set; }
        }

        public class GridDifference
        {
            public double? diff_avg_rsrp { get; set; }
            public double? diff_avg_rsrq { get; set; }
            public double? diff_avg_sinr { get; set; }
            public double? diff_median_rsrp { get; set; }
            public double? diff_median_rsrq { get; set; }
            public double? diff_median_sinr { get; set; }
            public double? diff_max_rsrp { get; set; }
            public double? diff_max_rsrq { get; set; }
            public double? diff_max_sinr { get; set; }
            public double? diff_mode_rsrp { get; set; }
            public double? diff_mode_rsrq { get; set; }
            public double? diff_mode_sinr { get; set; }
        }
    }
}
