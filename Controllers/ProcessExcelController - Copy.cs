using ExcelDataReader;
using ForecastModule.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace ForecastModule.Controllers
{
    public class ProcessExcelController : Controller
    {
        private static TimeZoneInfo INDIAN_ZONE = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        ApplicationDbContext db = null;
        CommonFunction cf = null;
        public ProcessExcelController(ApplicationDbContext context, CommonFunction _cf)
        {
            db = context;
            cf = _cf;
        }
        public bool Process(int ExcelId, string directoryPath, string fileName, int fileType, out string errorMsag, int state_id = 0, int discom_id = 0)
        {                 
            bool ret = ProcessFile(fileType, ExcelId, directoryPath, fileName, state_id, discom_id, out errorMsag);
            return ret;

        }        
        public bool ProcessFile(int fileType, int excelID, string directorypath,string fileName, int state_id, int discom_id, out string errorMsag)
        {
            bool IsValidSheet = true;
            List<string> errorList = new List<string>();
            errorMsag = "";
            int RowProcessed = 0;
            DataSet dsexcelRecords = new DataSet();
            IExcelDataReader reader = null;

            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            Stream FileStream = new FileStream(directorypath, FileMode.Open);
            try
            {
                if (System.IO.File.Exists(directorypath))
                {
                    if (fileName.EndsWith(".xls"))
                        reader = ExcelReaderFactory.CreateBinaryReader(FileStream);
                    else if (fileName.EndsWith(".xlsx"))
                        reader = ExcelReaderFactory.CreateOpenXmlReader(FileStream);
                    else
                    {
                        //retMessage.message = "The file format is not supported.";
                        //CustomException ex = new CustomException(retMessage.message);
                        // WriteExcelLog(excelId, "Critical", ex);
                    }
                    dsexcelRecords = reader.AsDataSet();
                    if (reader is not null)
                        reader.Close();

                    if (dsexcelRecords != null && dsexcelRecords.Tables.Count > 0)
                    {
                        if (fileType == 4)//EPS File
                        {
                            RowProcessed += ProcessEPSFile(dsexcelRecords, excelID, state_id, discom_id, out errorMsag);
                        }
                        else
                        {
                            for (int t = 0; t < dsexcelRecords.Tables.Count; t++)
                            {
                                string tableName = dsexcelRecords.Tables[t].TableName;

                                if (fileType == 1)
                                    IsValidSheet = ProcessDiscomLevel15minSheet(dsexcelRecords.Tables[t], excelID, out errorList);
                                else if (fileType == 2)
                                    IsValidSheet = ProcessOverallDiscom15minSheet(dsexcelRecords.Tables[t], excelID, out errorList);
                                else if (fileType == 3)
                                    IsValidSheet = ProcessIndependentVariableMonthly(dsexcelRecords.Tables[t], excelID, out errorList);
                                else if (fileType == 5)
                                    IsValidSheet = ProcessIndependentVariableYearly(dsexcelRecords.Tables[t], excelID, out errorList);
                                else if (fileType == 6)
                                    IsValidSheet = ProcessForm15Data(dsexcelRecords.Tables[t], excelID, out errorList);

                                break;

                            }
                        }
                    }

                    if (reader != null)
                    {
                        reader.Close();   // Close the reader
                        reader.Dispose(); // Dispose to free resources
                    }
                    FileStream.Close();  // Closes the stream
                }
            }catch(Exception ex)
            {
                IsValidSheet = false;
                errorMsag = "Exception " + ex.Message;
            }            
            if (errorList.Count > 0)
            {
                errorMsag += string.Join(Environment.NewLine, errorList);
            }

            return IsValidSheet;   
        }
        #region EPS Sharad - 18 Feb
        public int ProcessEPSFile(DataSet dsexcelRecords, int excelID, int stateId, int discomId, out string errorMsag)
        {            
            int RowProcessed = 0;
            List<string> errorList = new List<string>();
            errorMsag = "";
            for (int t = 0; t < dsexcelRecords.Tables.Count; t++)
            {

                try
                {
                    string sheet = dsexcelRecords.Tables[t].TableName;
                    if (sheet.ToLower().Contains("instructions"))//sheet == "Instructions")
                        continue;
                    else if (sheet.ToLower().Contains("data center"))  //if (sheet == "Data center")
                        RowProcessed += ProcessEPS_DataCenter(dsexcelRecords.Tables[t], excelID, stateId, discomId, out errorList);
                    else if (sheet.ToLower().Contains("hydrogen"))
                        ProcessEPS_Hydrogen(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    else if (sheet.ToLower().Contains("domestic"))
                        RowProcessed += ProcessEPS_Domestic(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    else if (sheet.ToLower().Contains("commercial"))
                        RowProcessed += ProcessEPS_Commercial(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    else if (sheet.ToLower().Contains("public lighting"))
                        RowProcessed += ProcessEPS_public_lighting(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    else if (sheet.ToLower().Contains("pwworks"))
                        RowProcessed += ProcessEPS_pw_works(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    else if (sheet.ToLower().Contains("industries lt"))
                        RowProcessed += ProcessEPS_industries_lt(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    else if (sheet.ToLower().Contains("industries ht"))
                        RowProcessed += ProcessEPS_industries_ht(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    else if (sheet.ToLower().Contains("open access excluding railways"))
                        RowProcessed += ProcessEPS_open_access(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    else if (sheet.ToLower().Contains("railways"))
                        RowProcessed += ProcessEPS_railways(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    else if (sheet.ToLower().Contains("bulk supply"))
                        RowProcessed += ProcessEPS_bulk_supply(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    else if (sheet.ToLower().Contains("solar roof top"))
                        RowProcessed += ProcessEPS_solar_rooftop(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    else if (sheet.ToLower().Contains("solar pump trajectory"))
                        RowProcessed += ProcessEPS_solar_pump_trajectory(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    else if (sheet.ToLower().Contains("lift irrigation"))
                        RowProcessed += ProcessEPS_lift_irrigation(dsexcelRecords.Tables[t], excelID, stateId, discomId);

                    else if (sheet.ToLower().Contains("irrigation"))
                        RowProcessed += ProcessEPS_irrigation(dsexcelRecords.Tables[t], excelID, stateId, discomId);

                    else if (sheet.ToLower().Contains("ev charging station"))
                        RowProcessed += ProcessEPS_ev_charging_station(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    else if (sheet.ToLower().Contains("ev trajectory"))
                        RowProcessed += ProcessEPS_ev_trajectory(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    else if (sheet.ToLower().Contains("peak"))
                    {
                        //this function needs to be implemented
                        RowProcessed += ProcessEPS_peak(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                    }
                    else if (sheet.ToLower().Contains("t&d loss"))
                        RowProcessed += ProcessEPS_td_loss(dsexcelRecords.Tables[t], excelID, stateId, discomId);
                }
                catch (Exception ex)
                {
                    errorMsag = "Excel Upload - " + ex.Message;
                    //ExcelErrorLog(ExcelID, "Generic", "", Error);
                }
            }
            if (errorList.Count > 0)
            {
                errorMsag += string.Join(Environment.NewLine, errorList);                
            }
            return RowProcessed;
        }
        //eps1
        public int ProcessEPS_DataCenter(DataTable SheetData, int ExcelID, int state_id, int discom_id, out List<string> errorList)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "DataCenter - ";
            string result_message = "DataCenter - ";

            int type = 1; //actual
            bool isColValValid = true;
            errorList = new List<string>();
            using (var dbContextTransaction = db.Database.BeginTransaction())
            {
                foreach (DataRow row in SheetData.Rows)
                {
                    try
                    {
                        totlRowsProcessed++;

                        if (skipRows > 0)
                        {
                            string? tmp11 = row[1]?.ToString();
                            skipRows--;
                            continue;
                        }
                        if (!isColValValid) break;

                        rowid++;

                        string year = row[0].ToString();
                        string tmp = row[1].ToString();
                        int index = tmp.ToLower().IndexOf("forecast");
                        if (index != -1)
                            type = 2; //forecast
                        if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                            break;
                        else if (string.IsNullOrEmpty(year)) continue;

                        List<string> columnNames = new List<string> { "consumers_end_yr", "consumers_mid_yr", "consumers_inc", "consumers_agr_pct",
                        "load_end_yr", "load_mid_yr", "load_inc", "load_agr_pct", "ee_mu", "ee_agr_pct"};

                        var exists = db.eps_data_center.FirstOrDefault(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type) ?? new eps_data_center();
                        string? val = null;
                        int ColCount = 2;
                        foreach (var colName in columnNames)
                        {
                            val = GetColStringVal(row[ColCount], out isColValValid);
                            if (!isColValValid)
                            {
                                errorList.Add($"Row {rowid + 1}: Invalid value in column '{colName}'. File is NOT uploaded. Please fix the error and try again.");
                                break;
                            }
                            typeof(eps_data_center).GetProperty(colName)?.SetValue(exists, val);
                            ColCount++;
                        }

                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;
                        exists.tbl_upload_id = ExcelID;

                        if (exists.id > 0)
                        {
                            db.Entry(exists).State = EntityState.Modified;
                            rowsModified++;
                        }
                        else
                        {
                            db.eps_data_center.Add(exists);
                            rowsAdded++;
                        }
                    }
                    catch (Exception e)
                    {
                        //write to exception table
                        exceptionRaised++;
                        //write to exception table
                        error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                    }
                }//end for

                if (isColValValid)
                {
                    db.SaveChanges();
                    dbContextTransaction.Commit();
                }
                else
                    dbContextTransaction.Rollback();
            }

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;


            return totlRowsProcessed;
        }
        //eps 2
        public int ProcessEPS_Hydrogen(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "Hydrogen - ";
            string result_message = "Hydrogen - ";

            int type = 1; //actual
            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast

                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string load_end_yr = row[2].ToString();
                    string load_mid_yr = row[3].ToString();
                    string load_inc = row[4].ToString();
                    string load_agr_pct = row[5].ToString();

                    string ops_hrs = row[6].ToString();
                    string ops_hrs_inc = row[7].ToString();

                    string ee_mu = row[8].ToString();
                    string ee_agr_pct = row[9].ToString();

                    var exists = db.eps_hydrogen.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {

                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ops_hrs = ops_hrs;
                        exists.ops_hrs_inc = ops_hrs_inc;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_hydrogen();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ops_hrs = ops_hrs;
                        exists.ops_hrs_inc = ops_hrs_inc;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_hydrogen.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;
                    //write to exception table
                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }

        //eps 3
        public int ProcessEPS_Domestic(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;

            string error_message = "Domestic - ";
            string result_message = "Domestic - ";

            int exceptionRaised = 0;
            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast
                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string population = row[2].ToString();
                    string consumers_end_yr = row[3].ToString();
                    string consumers_mid_yr = row[4].ToString();
                    string consumers_inc = row[5].ToString();
                    string consumers_agr_pct = row[6].ToString();

                    string specific_ee_kwh = row[7].ToString();
                    string specific_ee_inc = row[8].ToString();

                    string ee_mu = row[9].ToString();
                    string ee_agr_pct = row[10].ToString();

                    var exists = db.eps_domestic.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.population = population;
                        exists.consumers_end_yr = consumers_end_yr;
                        exists.consumers_mid_yr = consumers_mid_yr;
                        exists.consumers_inc = consumers_inc;
                        exists.consumers_agr_pct = consumers_agr_pct;

                        exists.specific_ee_kwh = specific_ee_kwh;
                        exists.specific_ee_inc = specific_ee_inc;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_domestic();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.population = population;
                        exists.consumers_end_yr = consumers_end_yr;
                        exists.consumers_mid_yr = consumers_mid_yr;
                        exists.consumers_inc = consumers_inc;
                        exists.consumers_agr_pct = consumers_agr_pct;

                        exists.specific_ee_kwh = specific_ee_kwh;
                        exists.specific_ee_inc = specific_ee_inc;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;

                        rowsAdded++;
                        db.eps_domestic.Add(exists);
                    }
                }
                catch (Exception e)
                {
                    exceptionRaised++;
                    //write to exception table
                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }

        //eps 4
        public int ProcessEPS_Commercial(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;

            string error_message = "Commercial - ";
            string result_message = "Commercial - ";

            int exceptionRaised = 0;

            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast
                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string load_end_yr = row[2].ToString();
                    string load_mid_yr = row[3].ToString();
                    string load_inc = row[4].ToString();
                    string load_agr_pct = row[5].ToString();

                    string ee_mu = row[6].ToString();
                    string ee_agr_pct = row[7].ToString();

                    var exists = db.eps_commercial.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {

                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;


                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_commercial();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;

                        rowsAdded++;
                        db.eps_commercial.Add(exists);
                    }
                }
                catch (Exception e)
                {
                    exceptionRaised++;
                    //write to exception table
                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_Commercial

        //eps 5
        public int ProcessEPS_public_lighting(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "Public Lighting - ";
            string result_message = "Public Lighting - ";

            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast
                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string load_end_yr = row[2].ToString();
                    string load_mid_yr = row[3].ToString();
                    string load_inc = row[4].ToString();
                    string load_agr_pct = row[5].ToString();

                    string ops_hrs = row[6].ToString();
                    string ops_hrs_inc = row[7].ToString();

                    string ee_mu = row[8].ToString();
                    string ee_agr_pct = row[9].ToString();

                    var exists = db.eps_public_lighting.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ops_hrs = ops_hrs;
                        exists.ops_hrs_inc = ops_hrs_inc;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_public_lighting();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ops_hrs = ops_hrs;
                        exists.ops_hrs_inc = ops_hrs_inc;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_public_lighting.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_public_lighting

        //eps 6
        public int ProcessEPS_pw_works(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;


            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "PW Works - ";
            string result_message = "PW Works - ";

            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast
                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string load_end_yr = row[2].ToString();
                    string load_mid_yr = row[3].ToString();
                    string load_inc = row[4].ToString();
                    string load_agr_pct = row[5].ToString();

                    string ops_hrs = row[6].ToString();
                    string ops_hrs_inc = row[7].ToString();

                    string ee_mu = row[8].ToString();
                    string ee_agr_pct = row[9].ToString();

                    var exists = db.eps_pw_works.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ops_hrs = ops_hrs;
                        exists.ops_hrs_inc = ops_hrs_inc;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_pw_works();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ops_hrs = ops_hrs;
                        exists.ops_hrs_inc = ops_hrs_inc;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_pw_works.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_pw_works


        //eps 7
        public int ProcessEPS_industries_lt(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;


            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "Industries LT - ";
            string result_message = "Industries LT - ";

            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast
                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string load_end_yr = row[2].ToString();
                    string load_mid_yr = row[3].ToString();
                    string load_inc = row[4].ToString();
                    string load_agr_pct = row[5].ToString();

                    string ops_hrs = row[6].ToString();
                    string ops_hrs_inc = row[7].ToString();

                    string ee_mu = row[8].ToString();
                    string ee_agr_pct = row[9].ToString();

                    var exists = db.eps_industries_lt.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ops_hrs = ops_hrs;
                        exists.ops_hrs_inc = ops_hrs_inc;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_industries_lt();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ops_hrs = ops_hrs;
                        exists.ops_hrs_inc = ops_hrs_inc;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_industries_lt.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_industries_lt

        //eps 8
        public int ProcessEPS_industries_ht(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "Industries HT - ";
            string result_message = "Industries HT - ";

            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast
                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string load_end_yr = row[2].ToString();
                    string load_mid_yr = row[3].ToString();
                    string load_inc = row[4].ToString();
                    string load_agr_pct = row[5].ToString();

                    string ops_hrs = row[6].ToString();
                    string ops_hrs_inc = row[7].ToString();

                    string ee_mu = row[8].ToString();
                    string ee_agr_pct = row[9].ToString();

                    var exists = db.eps_industries_ht.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ops_hrs = ops_hrs;
                        exists.ops_hrs_inc = ops_hrs_inc;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_industries_ht();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ops_hrs = ops_hrs;
                        exists.ops_hrs_inc = ops_hrs_inc;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_industries_ht.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_industries_ht

        //eps 9
        public int ProcessEPS_open_access(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "Open access - ";
            string result_message = "Open access - ";

            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast
                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string ee_mu = row[2].ToString();
                    string ee_agr_pct = row[3].ToString();

                    var exists = db.eps_open_access.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_open_access();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_open_access.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//eps_open_access

        //eps 10
        public int ProcessEPS_railways(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;


            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "Industries HT - ";
            string result_message = "Industries HT - ";

            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast
                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string load_end_yr = row[2].ToString();
                    string load_mid_yr = row[3].ToString();
                    string load_inc = row[4].ToString();
                    string load_agr_pct = row[5].ToString();

                    string ee_open_access_mu = row[6].ToString();
                    string ee_open_access_pct = row[7].ToString();

                    string ee_wo_open_access_mu = row[8].ToString();
                    string ee_wo_open_access_pct = row[9].ToString();

                    string energy_mu = row[10].ToString();
                    string energy_agr_pct = row[11].ToString();

                    var exists = db.eps_railways.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ee_open_access_mu = ee_open_access_mu;
                        exists.ee_open_access_pct = ee_open_access_pct;

                        exists.ee_wo_open_access_mu = ee_wo_open_access_mu;
                        exists.ee_wo_open_access_pct = ee_wo_open_access_pct;

                        exists.energy_mu = energy_mu;
                        exists.energy_agr_pct = energy_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_railways();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ee_open_access_mu = ee_open_access_mu;
                        exists.ee_open_access_pct = ee_open_access_pct;

                        exists.ee_wo_open_access_mu = ee_wo_open_access_mu;
                        exists.ee_wo_open_access_pct = ee_wo_open_access_pct;

                        exists.energy_mu = energy_mu;
                        exists.energy_agr_pct = energy_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_railways.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_railways

        //eps 11
        public int ProcessEPS_bulk_supply(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "Bulk Supply - ";
            string result_message = "Bulk Supply - ";

            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast
                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string load_end_yr = row[2].ToString();
                    string load_mid_yr = row[3].ToString();
                    string load_inc = row[4].ToString();
                    string load_agr_pct = row[5].ToString();

                    string ee_mu = row[6].ToString();
                    string ee_agr_pct = row[7].ToString();

                    var exists = db.eps_bulk_supply.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_bulk_supply();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_bulk_supply.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_bulk_supply

        //eps 12
        public int ProcessEPS_solar_rooftop(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "solar_rooftop - ";
            string result_message = "solar_rooftop - ";

            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast

                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string load_end_yr = row[2].ToString();
                    string load_mid_yr = row[3].ToString();
                    string load_inc = row[4].ToString();
                    string load_agr_pct = row[5].ToString();

                    var exists = db.eps_solar_rooftop.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_solar_rooftop();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_solar_rooftop.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_solar_rooftop
         //eps 13
        public int ProcessEPS_solar_pump_trajectory(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "Solar Pump Traj - ";
            string result_message = "Solar Pump Traj - ";

            int type = 1; //actual           

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast
                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string pumps_end_yr = row[2].ToString();
                    string pumps_mid_yr = row[3].ToString();
                    string pumps_inc = row[4].ToString();
                    string pumps_agr_pct = row[5].ToString();

                    string ee_mu = row[6].ToString();
                    string ee_agr_pct = row[7].ToString();

                    var exists = db.eps_solar_pump_trajectory.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.pumps_end_yr = pumps_end_yr;
                        exists.pumps_mid_yr = pumps_mid_yr;
                        exists.pumps_inc = pumps_inc;
                        exists.pumps_agr_pct = pumps_agr_pct;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_solar_pump_trajectory();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.pumps_end_yr = pumps_end_yr;
                        exists.pumps_mid_yr = pumps_mid_yr;
                        exists.pumps_inc = pumps_inc;
                        exists.pumps_agr_pct = pumps_agr_pct;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_solar_pump_trajectory.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_solar_pump_trajectory

        //eps 14
        public int ProcessEPS_ev_charging_station(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "ev_charging_station - ";
            string result_message = "ev_charging_station - ";

            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast
                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string cs_end_yr = row[2].ToString();
                    string cs_mid_yr = row[3].ToString();
                    string cs_inc = row[4].ToString();
                    string cs_agr_pct = row[5].ToString();

                    string load_end_yr = row[6].ToString();
                    string load_mid_yr = row[7].ToString();
                    string load_inc = row[8].ToString();
                    string load_agr_pct = row[9].ToString();

                    string ops_hrs = row[10].ToString();
                    string ops_hrs_inc = row[11].ToString();

                    string ee_mu = row[12].ToString();
                    string ee_agr_pct = row[13].ToString();

                    var exists = db.eps_ev_charging_station.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.cs_end_yr = cs_end_yr;
                        exists.cs_mid_yr = cs_mid_yr;
                        exists.cs_inc = cs_inc;
                        exists.cs_agr_pct = cs_agr_pct;

                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ops_hrs = ops_hrs;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.ee_mu = ee_mu;
                        exists.ops_hrs_inc = ops_hrs_inc;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_ev_charging_station();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.cs_end_yr = cs_end_yr;
                        exists.cs_mid_yr = cs_mid_yr;
                        exists.cs_inc = cs_inc;
                        exists.cs_agr_pct = cs_agr_pct;

                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ops_hrs = ops_hrs;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.ee_mu = ee_mu;
                        exists.ops_hrs_inc = ops_hrs_inc;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_ev_charging_station.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_ev_charging_station
        //eps 15
        public int ProcessEPS_ev_trajectory(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "ev_charging_station - ";
            string result_message = "ev_charging_station - ";

            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast
                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string ev_end_yr = row[2].ToString();
                    string ev_mid_yr = row[3].ToString();
                    string ev_inc = row[4].ToString();
                    string ev_agr_pct = row[5].ToString();

                    var exists = db.eps_ev_trajectory.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.ev_end_yr = ev_end_yr;
                        exists.ev_mid_yr = ev_mid_yr;
                        exists.ev_inc = ev_inc;
                        exists.ev_agr_pct = ev_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_ev_trajectory();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.ev_end_yr = ev_end_yr;
                        exists.ev_mid_yr = ev_mid_yr;
                        exists.ev_inc = ev_inc;
                        exists.ev_agr_pct = ev_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_ev_trajectory.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_ev_trajectory
         //eps 16
        public int ProcessEPS_peak(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 7;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "peak - ";
            string result_message = "peak - ";


            Dictionary<string, int> dctYearWithType = new Dictionary<string, int>();
            
            string[,] data = new string[7, 50]; //X -> 6 rows
            int dataIndex = 0;

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }
                    rowid++;

                    string tmp = row[0].ToString();
                    if (string.IsNullOrEmpty(tmp) && dctYearWithType.Count > 0)
                        break;

                    int index = tmp.ToLower().IndexOf("description");
                    if (index != -1 && dctYearWithType.Count == 0)
                    {
                        int type = 1; //actual
                        DataRow previousRow = SheetData.Rows[totlRowsProcessed - 2];
                        for (int i = 1; i < row.ItemArray.Length - 1; i++)
                        {
                            string year = row[i].ToString();
                            if (string.IsNullOrEmpty(year) && i > 1)
                                break;

                            string PrevTmp = previousRow[i].ToString();
                            int index1 = PrevTmp.ToLower().IndexOf("forecast");
                            if (index1 != -1)
                                type = 2; //forecast
                            if (!dctYearWithType.ContainsKey(year))
                            {
                                dctYearWithType.Add(year, type);
                            }

                        }
                        continue;
                    }
                    if (dctYearWithType.Count > 0)
                    {
                        int colIndex = 0;
                        for (int k = 0; k < dctYearWithType.Count; k++)
                        {
                            string val1 = row[1 + colIndex].ToString();

                            data[dataIndex, colIndex] = val1;

                            colIndex++;
                        }//end while

                        dataIndex++;
                        if (dataIndex == 7)
                        {
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            int fyIndex = 0;
            foreach (var kvp in dctYearWithType)
            {

                var exists = db.eps_peak.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == kvp.Key && e.type == kvp.Value).FirstOrDefault();
                if (exists != null)
                {

                    exists.annual_peak_date = data[0, fyIndex];
                    exists.annual_peak_time = data[1, fyIndex];
                    exists.peak_load_mw = data[2, fyIndex];
                    exists.load_shedding_mw = data[3, fyIndex];

                    exists.unrestricted_peak_load = data[4, fyIndex];
                    exists.annual_energy_req_mu = data[5, fyIndex];
                    exists.energy_cuts = data[6, fyIndex];

                    exists.tbl_upload_id = ExcelID;
                    db.Entry(exists).State = EntityState.Modified;

                    rowsModified++;
                }
                else
                {
                    exists = new eps_peak();
                    exists.state_id = state_id;
                    exists.year = kvp.Key;
                    exists.discom_id = discom_id;
                    exists.type = kvp.Value;
                    exists.annual_peak_date = data[0, fyIndex];
                    exists.annual_peak_time = data[1, fyIndex];
                    exists.peak_load_mw = data[2, fyIndex];
                    exists.load_shedding_mw = data[3, fyIndex];

                    exists.unrestricted_peak_load = data[4, fyIndex];
                    exists.annual_energy_req_mu = data[5, fyIndex];
                    exists.energy_cuts = data[6, fyIndex];
                    exists.tbl_upload_id = ExcelID;
                    db.eps_peak.Add(exists);

                    rowsAdded++;
                }
                fyIndex++;
            }

            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_peak
        //eps 17
        public int ProcessEPS_lift_irrigation(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "lift_irrigation - ";
            string result_message = "lift_irrigation - ";

            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    int index1 = tmp.ToLower().IndexOf("Prov.");
                    int index2 = tmp.ToLower().IndexOf("short &medium term");
                    int index3 = tmp.ToLower().IndexOf("long term");
                    int index4 = tmp.ToLower().IndexOf("perspective");


                    if (index != -1 || index1 != -1 || index2 != -1 || index3 != -1 || index4 != -1)
                        type = 2; //forecast
                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string load_end_yr = row[2].ToString();
                    string load_mid_yr = row[3].ToString();
                    string load_inc = row[4].ToString();
                    string load_agr_pct = row[5].ToString();

                    string ee_mu = row[6].ToString();
                    string ee_agr_pct = row[7].ToString();

                    var exists = db.eps_lift_irrigation.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_lift_irrigation();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.load_end_yr = load_end_yr;
                        exists.load_mid_yr = load_mid_yr;
                        exists.load_inc = load_inc;
                        exists.load_agr_pct = load_agr_pct;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_lift_irrigation.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_lift_irrigation

        //eps 18
        public int ProcessEPS_irrigation(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "irrigation - ";
            string result_message = "irrigation - ";

            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast
                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string pumps_end_yr = row[2].ToString();
                    string pumps_mid_yr = row[3].ToString();
                    string pumps_inc = row[4].ToString();
                    string pumps_agr_pct = row[5].ToString();

                    string avg_capacity_pumpsets = row[6].ToString();
                    string hrs_ops = row[7].ToString();

                    string ee_mu = row[8].ToString();
                    string ee_agr_pct = row[9].ToString();

                    var exists = db.eps_irrigation.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.pumps_end_yr = pumps_end_yr;
                        exists.pumps_mid_yr = pumps_mid_yr;
                        exists.pumps_inc = pumps_inc;
                        exists.pumps_agr_pct = pumps_agr_pct;

                        exists.avg_capacity_pumpsets = avg_capacity_pumpsets;
                        exists.hrs_ops = hrs_ops;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_irrigation();

                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.pumps_end_yr = pumps_end_yr;
                        exists.pumps_mid_yr = pumps_mid_yr;
                        exists.pumps_inc = pumps_inc;
                        exists.pumps_agr_pct = pumps_agr_pct;

                        exists.avg_capacity_pumpsets = avg_capacity_pumpsets;
                        exists.hrs_ops = hrs_ops;

                        exists.ee_mu = ee_mu;
                        exists.ee_agr_pct = ee_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_irrigation.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_irrigation

        //eps 19
        public int ProcessEPS_td_loss(DataTable SheetData, int ExcelID, int state_id, int discom_id)
        {
            int rowid = 0;
            int skipRows = 9;
            int totlRowsProcessed = 0;

            int rowsAdded = 0;
            int rowsModified = 0;
            int exceptionRaised = 0;
            string error_message = "td_loss - ";
            string result_message = "td_loss - ";

            int type = 1; //actual

            foreach (DataRow row in SheetData.Rows)
            {
                try
                {
                    totlRowsProcessed++;

                    if (skipRows > 0)
                    {
                        string tmp11 = row[1].ToString();
                        skipRows--;
                        continue;
                    }

                    rowid++;

                    string year = row[0].ToString();
                    string tmp = row[1].ToString();
                    int index = tmp.ToLower().IndexOf("forecast");
                    if (index != -1)
                        type = 2; //forecast

                    if (string.IsNullOrEmpty(year) && (rowsModified > 0 || rowsAdded > 0))
                        break;
                    else if (string.IsNullOrEmpty(year)) continue;

                    string td_loss_mu = row[2].ToString();
                    string td_agr_pct = row[3].ToString();


                    var exists = db.eps_td_loss.Where(e => e.state_id == state_id && e.discom_id == discom_id && e.year == year && e.type == type).FirstOrDefault();
                    if (exists != null)
                    {
                        exists.td_loss_mu = td_loss_mu;
                        exists.td_agr_pct = td_agr_pct;


                        exists.tbl_upload_id = ExcelID;
                        db.Entry(exists).State = EntityState.Modified;

                        rowsModified++;
                    }
                    else
                    {
                        exists = new eps_td_loss();
                        exists.state_id = state_id;
                        exists.year = year;
                        exists.discom_id = discom_id;
                        exists.type = type;

                        exists.td_loss_mu = td_loss_mu;
                        exists.td_agr_pct = td_agr_pct;

                        exists.tbl_upload_id = ExcelID;
                        db.eps_td_loss.Add(exists);

                        rowsAdded++;
                    }
                }
                catch (Exception e)
                {
                    //write to exception table
                    exceptionRaised++;

                    error_message = error_message + " Row No - " + totlRowsProcessed + " Error - " + e.Message;
                }
            }//end for
            db.SaveChanges();

            result_message = result_message + " Total Rows = " + totlRowsProcessed + " Rows Added = " + rowsAdded + " Updated = " + rowsModified + " Errors = " + exceptionRaised;

            return totlRowsProcessed;
        }//ProcessEPS_td_loss
        #endregion
        #region Calculate Sheet-> DiscomLevel 15 min
        public bool ProcessDiscomLevel15minSheet(DataTable tblSheet, int ExcelID,out List<string> errorList)
        {           
            List<string> DateArr = new List<string>();
            Dictionary<string, int> discomNameArray = new Dictionary<string, int>();
            int skipRows = 1;
            int totlRowsProcessed = 0;
            int numberOfDiscoms = 0;
            errorList = new List<string>();
            bool isColValValid = true;
            using (var dbContextTransaction = db.Database.BeginTransaction())
            {
               
                for (int rowid = 0; rowid < tblSheet.Rows.Count; rowid++)
                {
                    if (!isColValValid)
                        break;

                    try
                    {
                        totlRowsProcessed++;
                        if (skipRows > 0)
                        {
                            skipRows--;
                            continue;
                        }

                        DataRow row = tblSheet.Rows[rowid];

                        string tmp = Convert.ToString(row[0] + "");
                        int index = tmp.ToLower().IndexOf("date");

                        if (DateArr.Count == 0 && index >= 0)//Calculate Date
                        {
                            //foreach (var date in row.ItemArray)
                            //{
                            //    if (date != null && date != DBNull.Value && date.ToString() != "DATE")
                            //    {
                            //        DateArr.Add(date + "");
                            //    }
                            //}
                            foreach (var date in row.ItemArray)
                            {

                                if (date != null && date != DBNull.Value && date.ToString().ToUpper() != "DATE")
                                {
                                    var date1 = date.ToString().Replace(" 12:00:00 AM", "");
                                    var date2 = date1.ToString().Replace(" 00:00:00", "");


                                    if (DateTime.TryParseExact(date2.ToString(),
                                                               new[] { "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd" },
                                                               CultureInfo.InvariantCulture,
                                                               DateTimeStyles.None,
                                                               out DateTime dateTime))
                                    {
                                        DateArr.Add(dateTime.ToString("yyyy-MM-dd")); // dd/MM/yyyy")); // Standardizing to dd/MM/yyyy
                                    }
                                    else
                                    {
                                        DateArr.Add(date2.ToString()); // Keep as is for non-date values
                                    }
                                }
                            }
                        }
                        else if (discomNameArray.Count == 0 && row[1].ToString().IndexOf("Discom") >= 0)
                        {
                            foreach (var discom in row.ItemArray)
                            {
                                if (discom != null && discom != DBNull.Value)
                                {
                                    string[] arr = discom.ToString().Split(new string[] { "Discom" }, StringSplitOptions.None);
                                    if (discomNameArray.Keys.Contains(arr[0]))
                                        break;
                                    if (arr.Length > 1)
                                    {
                                        int DiscomID = cf.GetDiscomIDByNameLike(arr[0]);
                                        discomNameArray.Add(arr[0], DiscomID);
                                        numberOfDiscoms++;
                                    }
                                }
                            }


                        }
                        else if (DateArr.Count > 0 && discomNameArray.Count > 0)
                        {
                            if (row[0] == DBNull.Value)
                                break;
                            int discomCount = 0;
                            int DateSelected = 0;

                            string strDate = readDateFromExcel(DateArr[DateSelected], ExcelID, totlRowsProcessed + "");
                            DateTime dt = DateTime.Now;
                            bool b = DateTime.TryParseExact(strDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
                           
                            for (int i = 1; i < row.ItemArray.Length; i++)
                            {
                                var val = row[i];

                                tbl_load_discom_level record = new tbl_load_discom_level();
                                record.tbl_upload_id = ExcelID;
                                record.discom_id = discomNameArray.ElementAt(discomCount).Value;
                                record.date = dt;

                                record.time = GetColValOld<string>(row[0], out isColValValid);

                                try
                                {
                                    record.time = record.time.Replace("31-12-1899", "").Trim();
                                    record.time = record.time.Split(' ')[1];
                                }catch(Exception ex1)
                                {

                                }
                                if (!isColValValid)
                                {
                                    errorList.Add($"Row {rowid + 1}: Blank value in column time. File is NOT uploaded. Please fix the error and try again.");
                                    break;
                                }

                                record.power_load = GetColVal<float>(row[i], out isColValValid);
                                if (!isColValValid)
                                {
                                    errorList.Add($"Row {rowid + 1}  Column {i + 1}: Blank value in power_load. File is NOT uploaded. Please fix the error and try again.");
                                    break;
                                }

                                discomCount++;
                                bool isLastCol = false;
                                if (discomCount >= discomNameArray.Count())
                                {
                                    discomCount = 0;
                                    DateSelected++;
                                    if (DateSelected >= DateArr.Count)
                                        isLastCol = true;
                                    else
                                    {
                                        strDate = readDateFromExcel(DateArr[DateSelected], ExcelID, totlRowsProcessed + "");
                                        b = DateTime.TryParseExact(strDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
                                    }
                                }

                                //Insert into database
                                var exists = db.tbl_load_discom_level.Where(e => e.discom_id == record.discom_id && e.date == record.date && e.time == record.time).FirstOrDefault();
                                if (exists != null)
                                {
                                    exists.power_load = record.power_load;
                                    exists.tbl_upload_id = ExcelID;
                                    db.Entry(exists).State = EntityState.Modified;
                                }
                                else
                                {
                                    record.state_id = 0;
                                    record.tbl_upload_id = ExcelID;
                                    db.tbl_load_discom_level.Add(record);
                                }
                                if (isLastCol) break;
                            }


                        }
                    }
                    catch (Exception e)
                    {
                        //write to exception table
                        isColValValid = false;
                        errorList.Add($"Row {rowid + 1}: Exception - {e.Message} File is NOT uploaded. Please fix the error and try again.");
                        break;
                        
                    }
                }//end for
                if (isColValValid)
                {
                    db.SaveChanges();
                    dbContextTransaction.Commit();
                }
                else
                    dbContextTransaction.Rollback();
            }
            return isColValValid;
        }
        #endregion
        #region Calculate Sheet-> Overall 15 min
        public bool ProcessOverallDiscom15minSheet(DataTable tblSheet, int ExcelID, out List<string> errorList)
        {
           
            List<string> DateArr = new List<string>();
            Dictionary<string, int> discomNameArray = new Dictionary<string, int>();
            int totlRowsProcessed = 0;
            int numberOfDiscoms = 0;
            int DateaCount = 0;
            errorList = new List<string>();
            bool isColValValid = true;

            int skipRows = 0;

            using (var dbContextTransaction = db.Database.BeginTransaction())
            {
                for (int rowid = 0; rowid < tblSheet.Rows.Count; rowid++)
                {
                    if (!isColValValid)
                        break;
                    try
                    {
                        totlRowsProcessed++;
                        if (skipRows > 0)
                        {
                            skipRows--;
                            continue;
                        }

                        DataRow row = tblSheet.Rows[rowid];
                        if (rowid == 0)
                        {
                            //foreach (var date in row.ItemArray)
                            //{
                            //    if (date != null && date != DBNull.Value)
                            //    {
                            //        DateArr.Add(date+"");
                            //    }
                            //    else if (DateaCount > 0)
                            //        break;
                            //    DateaCount++;
                            //}
                            //sharad added 13 march
                            foreach (var date in row.ItemArray)
                            {

                                if (date != null && date != DBNull.Value)
                                {
                                    var date1 = date.ToString().Replace(" 12:00:00 AM", "");
                                    var date2 = date1.ToString().Replace(" 00:00:00", "");

                                    if (DateTime.TryParseExact(date2.ToString(),
                                                               new[] { "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd" },
                                                               CultureInfo.InvariantCulture,
                                                               DateTimeStyles.None,
                                                               out DateTime dateTime))
                                    {
                                        DateArr.Add(dateTime.ToString("yyyy-MM-dd")); // dd/MM/yyyy")); // Standardizing to dd/MM/yyyy
                                    }
                                    else
                                    {
                                        DateArr.Add(date2.ToString()); // Keep as is for non-date values
                                    }
                                }
                                else if (DateaCount > 0)
                                    break;
                                DateaCount++;
                            }
                            continue;
                        }

                        string tmp = row[1].ToString();
                        if (discomNameArray.Count == 0 && row[0].ToString().IndexOf("Time") >= 0)
                        {
                            //foreach (var discom in row)
                            //{
                            var discom = row[1];
                            if (discom != null && discom != DBNull.Value)
                            {
                                string[] arr = discom.ToString().Split(new string[] { " " }, StringSplitOptions.None);
                                if (discomNameArray.Keys.Contains(arr[0]))
                                    break;
                                if (arr.Length > 1)
                                {
                                    int DiscomID = cf.GetStateIdOverallDiscomName(arr[0]);
                                    discomNameArray.Add(arr[0], DiscomID);
                                    numberOfDiscoms++;
                                }
                            }
                            //}


                        }
                        else if (DateArr.Count > 0 && discomNameArray.Count > 0)
                        {
                            if (row[0] == DBNull.Value)
                                break;
                            int discomCount = 0;
                            int DateSelected = 0;

                            string strDate = readDateFromExcel(DateArr[DateSelected], ExcelID, totlRowsProcessed + "");
                            DateTime dt = DateTime.Now;
                            bool b = DateTime.TryParseExact(strDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);

                            for (int i = 1; i < row.ItemArray.Length; i++)
                            {
                                var val = row[i];

                                tbl_load_overall record = new tbl_load_overall();
                                record.tbl_upload_id = ExcelID;
                                record.state_id = discomNameArray.ElementAt(discomCount).Value;
                                record.date = dt;

                                //record.time = row[0] + "";
                                //sharad added 13 march
                                //record.time = record.time.Replace("31-12-1899", "").Trim();
                               
                                //record.power_load = (row[i] == DBNull.Value || row[i].ToString() == "") ? 0 : float.Parse(row[i] + "");

                                ////////////////////
                                record.time = GetColValOld<string>(row[0], out isColValValid);

                                //record.time = record.time.Replace("31-12-1899", "").Trim();
                                try
                                {
                                    record.time = record.time.Replace("31-12-1899", "").Trim();
                                    record.time = record.time.Split(' ')[1];
                                }
                                catch (Exception ex1)
                                {

                                }
                                if (!isColValValid)
                                {
                                    errorList.Add($"Row {rowid + 1}: Blank value in column time.  File is NOT uploaded. Please fix the error and try again.");
                                    break;
                                }

                                record.power_load = GetColVal<float>(row[i], out isColValValid);
                                if (!isColValValid)
                                {
                                    errorList.Add($"Row {rowid + 1}  Column {i + 1}: Blank value in power_load. File is NOT uploaded. Please fix the error and try again.");
                                    break;
                                }
                                ///////////////////////


                                discomCount++;
                                bool isLastCol = false;
                                if (discomCount >= discomNameArray.Count())
                                {
                                    discomCount = 0;
                                    DateSelected++;
                                    if (DateSelected >= DateArr.Count)
                                        isLastCol = true;
                                    else
                                    {
                                        strDate = readDateFromExcel(DateArr[DateSelected], ExcelID, totlRowsProcessed + "");
                                        b = DateTime.TryParseExact(strDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
                                    }
                                }

                                //Insert into database
                                var exists = db.tbl_load_overall.Where(e => e.state_id == record.state_id && e.date == record.date && e.time == record.time).FirstOrDefault();
                                if (exists != null)
                                {
                                    exists.power_load = record.power_load;
                                    exists.tbl_upload_id = ExcelID;
                                    db.Entry(exists).State = EntityState.Modified;
                                }
                                else
                                {
                                    record.tbl_upload_id = ExcelID;
                                    db.tbl_load_overall.Add(record);
                                }
                                if (isLastCol) break;
                            }


                        }
                    }
                    catch (Exception e)
                    {
                        //write to exception table
                    }
                }//end for
                if (isColValValid)
                {
                    db.SaveChanges();
                    dbContextTransaction.Commit();
                }
                else
                    dbContextTransaction.Rollback();
                //db.SaveChanges();
            }
            return isColValValid;
        }
        #endregion
        #region Calculate Sheet-> Independent Variable Monthly
        public bool ProcessIndependentVariableMonthly(DataTable tblSheet, int ExcelID,out List<string> errorList)
        {
            bool isColValValid = true;
            int skipRows = 1;
            int totlRowsProcessed = 0;
            var StateList = db.tbl_state.Where(a => a.m_status_id == 1).OrderBy(a => a.state).ToList();
            errorList = new List<string>(); // Store errors
            using (var dbContextTransaction = db.Database.BeginTransaction())
            {
                for (int rowid = 0; rowid < tblSheet.Rows.Count; rowid++)
                {
                    try
                    {
                        if (skipRows > 0)
                        {
                            skipRows--;
                            continue;
                        }

                        DataRow row = tblSheet.Rows[rowid];
                        string stateName = row[0].ToString()?.Trim();

                        if (!isColValValid) break;

                        if (string.IsNullOrEmpty(stateName))
                        {
                            errorList.Add($"Row {rowid + 1}: Missing State.  File is NOT uploaded. Please fix the error and try again.");
                            continue;
                        }

                        var objState = StateList.FirstOrDefault(a => a.state.Equals(stateName, StringComparison.OrdinalIgnoreCase));
                        if (objState == null)
                        {
                            errorList.Add($"Row {rowid + 1}: Invalid State '{stateName}'. File is NOT uploaded. Please fix the error and try again.");
                            continue;
                        }
                        int year = 0;
                        int month = 0;

                        int? yearVal = GetColVal<int>(row[1], out isColValValid);
                        if (!isColValValid || yearVal == null || yearVal <= 0)
                        {
                            errorList.Add($"Row {rowid + 1}: Invalid Year. File is NOT uploaded. Please fix the error and try again.");
                            continue;
                        }
                        else
                            year = (int)yearVal;

                        int? monthVal = GetColVal<int>(row[2], out isColValValid);
                        if (!isColValValid || monthVal==null || monthVal < 1 || monthVal > 12)
                        {
                            errorList.Add($"Row {rowid + 1}: Invalid Month. File is NOT uploaded. Please fix the error and try again.");
                            continue;
                        }
                        else
                            month = (int)monthVal;

                        var exists = db.tbl_bulk_input_monthly.FirstOrDefault(e => e.state_id == objState.id && e.year == year && e.month == month)
                                     ?? new tbl_bulk_input_monthly();

                        decimal? val;
                        List<string> columnNames = new List<string> {
                "iip_manufacturing", "iip_mining", "iip_electricity", "iip_general", "cpi_rural_urban"
            };

                        int ColCount = 3;
                        foreach (var colName in columnNames)
                        {
                            val = GetColVal<decimal>(row[ColCount], out isColValValid);
                            if (!isColValValid)
                            {
                                errorList.Add($"Row {rowid + 1}: Invalid value in column '{colName}'. File is NOT uploaded. Please fix the error and try again.");
                            }
                            typeof(tbl_bulk_input_monthly).GetProperty(colName)?.SetValue(exists, val);
                            ColCount++;
                        }

                        exists.state_id = objState.id;
                        exists.year = year;
                        exists.month = month;
                        exists.tbl_upload_id = ExcelID;

                        if (exists.id > 0)
                            db.Entry(exists).State = EntityState.Modified;
                        else
                            db.tbl_bulk_input_monthly.Add(exists);

                        totlRowsProcessed++;
                    }
                    catch (Exception e)
                    {
                        errorList.Add($"Row {rowid + 1}: Exception - {e.Message} File is NOT uploaded. Please fix the error and try again.");
                    }
                }
                if (isColValValid)
                {
                    db.SaveChanges();
                    dbContextTransaction.Commit();
                }
                else
                    dbContextTransaction.Rollback();
            }

            return isColValValid;
        }

        #endregion
        #region Calculate Sheet-> Independent Variable yearly
        public bool ProcessIndependentVariableYearly(DataTable tblSheet, int ExcelID,out List<string> errorList)
        {
            bool isColValValid = true;

            int totlRowsProcessed = 0;
            int skipRows = 1;
            var StateList = db.tbl_state.Where(a => a.m_status_id == 1).OrderBy(a => a.state).ToList();
            errorList = new List<string>();
            using (var dbContextTransaction = db.Database.BeginTransaction())
            {

                for (int rowid = 0; rowid < tblSheet.Rows.Count; rowid++)
                {
                    try
                    {
                        if (skipRows > 0)
                        {
                            skipRows--;
                            continue;
                        }

                        DataRow row = tblSheet.Rows[rowid];
                        string tmp = row[0].ToString()?.Trim();

                        if (!isColValValid) break;

                        if (!string.IsNullOrEmpty(tmp))
                        {
                            var objState = StateList.FirstOrDefault(a => a.state.Equals(tmp, StringComparison.OrdinalIgnoreCase));
                            if (objState != null)
                            {

                                string DiscomName = GetColValOld<string>(row[3], out isColValValid);
                                if (!isColValValid || string.IsNullOrEmpty(DiscomName))
                                {
                                    errorList.Add($"Row {rowid + 1}: Invalid or missing Discom Name. File is NOT uploaded. Please fix the error and try again.");
                                    break;
                                }
                                int year = 0;
                                int DiscomId = cf.GetDiscomIDByShortName(DiscomName, objState.id);
                                int? yearVal = GetColVal<int>(row[2], out isColValValid);
                                if (!isColValValid || yearVal == null || yearVal <= 0)
                                {
                                    errorList.Add($"Row {rowid + 1}: Invalid Year. File is NOT uploaded. Please fix the error and try again.");
                                    break;
                                }
                                else
                                    year = (int)yearVal;

                                var exists = db.tbl_bulk_input_yearly.FirstOrDefault(e =>
                                    e.state_id == objState.id && e.year == year && e.discom_id == DiscomId)
                                    ?? new tbl_bulk_input_yearly();

                                exists.fy = GetColValOld<string>(row[1], out isColValValid);
                                if (!isColValValid)
                                {
                                    errorList.Add($"Row {rowid + 1}: Invalid value in column Fy. File is NOT uploaded. Please fix the error and try again.");
                                    break;
                                }

                                int ColCount = 4;
                                decimal? val;
                                List<string> columnNames = new List<string> {
                        "total_pumpsets_tubewells", "e_price", "agri_gva", "commercial_gva", "industrial_gva", "gdva",
                        "population", "per_capita_gdva", "gdp_crops", "gdp_livestock", "gdp_forestry_logging", "gdp_fisheries",
                        "gdp_mining_quarrying", "gdp_manufacturing", "gdp_electricity_gas_watersupply", "gdp_construction",
                        "gdp_trade_hotels_restaurants", "gdp_railways", "gdp_transport", "gdp_storage", "gdp_communication",
                        "gdp_financialservices", "gdp_realownershipofdwelbser_legal", "gdp_publicadministration",
                        "gdp_otherservices", "gdp_totalgdva", "gdp_addproducttax", "gdp_lesssubsidy", "gdp_gddp",
                        "gdp_percapitagrossincome", "gdp_primary", "gdp_secondary", "gdp_tertiary", "gdp_agri_allied",
                        "gdp_industry"
                    };

                                foreach (var colName in columnNames)
                                {
                                    val = GetColVal<decimal>(row[ColCount], out isColValValid);
                                    if (!isColValValid)
                                    {
                                        errorList.Add($"Row {rowid + 1}: Invalid value in column '{colName}'. File is NOT uploaded. Please fix the error and try again.");
                                        break;
                                    }
                                    typeof(tbl_bulk_input_yearly).GetProperty(colName)?.SetValue(exists, val);
                                    ColCount++;
                                }

                                exists.state_id = objState.id;

                                exists.year = year;
                                exists.discom_id = DiscomId;
                                exists.tbl_upload_id = ExcelID;

                                if (exists.id > 0)
                                    db.Entry(exists).State = EntityState.Modified;
                                else
                                    db.tbl_bulk_input_yearly.Add(exists);

                                totlRowsProcessed++;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        errorList.Add($"Row {rowid + 1}: Exception - {e.Message}");
                        break;
                    }
                }
                if (isColValValid)
                {
                    db.SaveChanges();
                    dbContextTransaction.Commit();
                }
                else
                    dbContextTransaction.Rollback();
            }
            return isColValValid;
        }

        #endregion
        #region Calculate Sheet-> Form 15
        public bool ProcessForm15Data(DataTable tblSheet, int ExcelID,out List<string> errorList)
        {
            bool isColValValid = true;
            int totlRowsProcessed = 0;
            var StateList = db.tbl_state.Where(a => a.m_status_id == 1).OrderBy(a => a.state).ToList();
            errorList = new List<string>();
            using (var dbContextTransaction = db.Database.BeginTransaction())
            {
                for (int rowid = 0; rowid < tblSheet.Rows.Count; rowid++)
                {
                    try
                    {
                        totlRowsProcessed++;
                        DataRow row = tblSheet.Rows[rowid];
                        string tmp = row[0].ToString()?.Trim();
                        if (!isColValValid) break;

                        if (!string.IsNullOrEmpty(tmp))
                        {
                            var objState = StateList.FirstOrDefault(a => a.state.Equals(tmp, StringComparison.OrdinalIgnoreCase));
                            if (objState != null)
                            {
                                string DiscomName = GetColValOld<string>(row[1], out isColValValid);
                                if (!isColValValid || string.IsNullOrEmpty(DiscomName))
                                {
                                    errorList.Add($"Row {rowid + 1}: Invalid or missing Discom Name. File is NOT uploaded. Please fix the error and try again.");
                                    break;
                                }
                                int year = 0;
                                int month = 0;
                                int DiscomId = cf.GetDiscomIDByShortName(DiscomName, objState.id);
                                int? yearVal = GetColVal<int>(row[2], out isColValValid);
                                if (!isColValValid || yearVal == null || yearVal <= 0)
                                {
                                    errorList.Add($"Row {rowid + 1}: Invalid Year. File is NOT uploaded. Please fix the error and try again.");
                                    break;
                                }
                                else
                                    year = (int)yearVal;

                                
                                int? monthVal =  GetColVal<int>(row[3], out isColValValid);
                                if (!isColValValid || monthVal==null || monthVal < 1 || monthVal > 12)
                                {
                                    errorList.Add($"Row {rowid + 1}: Invalid Month. File is NOT uploaded. Please fix the error and try again.");
                                    break;
                                }
                                else
                                    month=(int)monthVal;

                                  var exists = db.tbl_load_form_15.FirstOrDefault(e =>
                                    e.state_id == objState.id && e.year == year && e.month == month && e.discom_id == DiscomId)
                                    ?? new tbl_load_form_15();

                                int ColCount = 4;
                                decimal? val;
                                List<string> columnNames = new List<string> { "domestic", "commercial", "public_ligting", "public_water_works",
                        "irrigation", "lt_industries", "ht_industries", "railway_traction", "bulk_supply", "others",
                        "pvt_licensees", "entities_in_state", "entities_outside_state", "total_energy_consumption",
                        "td_losses", "td_losses_in_percent", "energy_req", "load_factor", "peak_load" };

                                foreach (var colName in columnNames)
                                {
                                    val = GetColVal<decimal>(row[ColCount], out isColValValid);
                                    if (!isColValValid)
                                    {
                                        errorList.Add($"Row {rowid + 1}: Invalid value in column '{colName}'. File is NOT uploaded. Please fix the error and try again.");
                                        break;
                                    }
                                    typeof(tbl_load_form_15).GetProperty(colName)?.SetValue(exists, val);
                                    ColCount++;
                                }

                                exists.state_id = objState.id;
                                exists.year = year;
                                exists.month = month;
                                exists.discom_id = DiscomId;
                                exists.tbl_upload_id = ExcelID;

                                if (exists.id > 0)
                                    db.Entry(exists).State = EntityState.Modified;
                                else
                                    db.tbl_load_form_15.Add(exists);
                            }

                        }
                    }
                    catch (Exception e)
                    {
                        errorList.Add($"Row {rowid + 1}: Exception - {e.Message}  File is NOT uploaded. Please fix the error and try again.");
                    }
                }
                if (isColValValid)
                {
                    db.SaveChanges();
                    dbContextTransaction.Commit();
                }
                else
                    dbContextTransaction.Rollback();
            }

            return isColValValid;
        }

        #endregion

        #region Excel Read Methods
        public string readDateFromExcel(string val, int ExcelID, string Row_no)
        {
            string correcteddate = val;
            if (val != "" && !val.ToLower().Equals("nil") && !val.ToLower().Equals("na") && !val.ToLower().Equals("-") && !val.ToLower().Contains("for") && !val.ToLower().Contains("the") && !val.ToLower().Contains("yet") && !val.ToLower().Contains("not") && !val.ToLower().Contains("vacancy"))
            {
                try
                {
                    double d = double.Parse(val);
                    DateTime conv = DateTime.FromOADate(d);
                    correcteddate = conv.ToString("dd-MM-yyyy");//.ToShortDateString(); //ToString("dd/MM/yyyy");
                }
                catch (Exception ex)
                {
                    DateTime dateTime;
                    if (DateTime.TryParseExact(val, "dd/MMM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                    {
                        correcteddate = dateTime.ToString("dd-MM-yyyy");
                    }
                    else if (DateTime.TryParseExact(val, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                    {
                        correcteddate = dateTime.ToString("dd-MM-yyyy");
                    }
                    else if (DateTime.TryParseExact(val, "dd MMM yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                    {
                        correcteddate = dateTime.ToString("dd-MM-yyyy");
                    }
                    else if (DateTime.TryParseExact(val, "d MMM yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                    {
                        correcteddate = dateTime.ToString("dd-MM-yyyy");
                    }
                    else if (DateTime.TryParseExact(val, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                    {
                        correcteddate = dateTime.ToString("dd-MM-yyyy");
                    }
                    else if (val.Contains("-") || val.Contains("/"))
                    {
                        string[] arr = { };
                        if (val.Contains("-"))
                            arr = val.Split('-');
                        else if (val.Contains("/"))
                            arr = val.Split('/');

                        if (arr.Length == 3)
                        {
                            int dd = Convert.ToInt32(arr[0]);
                            int mm;
                            if (int.TryParse(arr[1], out mm))
                            {
                                mm = Convert.ToInt32(arr[1]);
                            }
                            else
                            {
                                if (arr[1].Trim().Length == 3)
                                {
                                    string t = arr[1].Trim().ToLower();
                                    mm = getMonth(t);
                                }
                                else if (arr[1].Trim().Length > 3)
                                {
                                    string t = arr[1].Trim().ToLower();
                                    mm = getMonth1(t);
                                }
                            }

                            int yy = Convert.ToInt32(arr[2]);
                            if (arr[2].Length == 2)
                            {
                                arr[2] = "20" + arr[2];
                                yy = Convert.ToInt32(arr[2]);
                            }
                            if (mm > 12)
                            {
                                int t = dd;
                                dd = mm;
                                mm = t;
                            }
                            if (mm < 10)
                            {
                                if (dd < 10)
                                    correcteddate = "0" + dd + "-0" + mm + "-" + yy;
                                else
                                    correcteddate = dd + "-0" + mm + "-" + yy;
                            }
                            else
                            {
                                if (dd < 10)
                                    correcteddate = "0" + dd + "-" + mm + "-" + yy;
                                else
                                    correcteddate = dd + "-" + mm + "-" + yy;
                            }
                        }
                    }
                    else if (val.Contains("."))
                    {
                        string[] arr = val.Split('.');
                        if (arr.Length == 3)
                        {
                            int dd = Convert.ToInt32(arr[0]);
                            int mm;
                            if (int.TryParse(arr[1], out mm))
                            {
                                mm = Convert.ToInt32(arr[1]);
                            }
                            int yy = Convert.ToInt32(arr[2]);

                            if (mm < 10)
                            {
                                if (dd < 10)
                                    correcteddate = "0" + dd + "-0" + mm + "-" + yy;
                                else
                                    correcteddate = dd + "-0" + mm + "-" + yy;
                            }
                            else
                            {
                                if (dd < 10)
                                    correcteddate = "0" + dd + "-" + mm + "-" + yy;
                                else
                                    correcteddate = dd + "-" + mm + "-" + yy;
                            }
                        }
                    }
                    else if (val.Contains("th") || val.Contains("rd"))
                    {
                        //11th May, 2018
                        string[] arr;
                        if (val.Contains("th"))
                            arr = val.Split(new string[] { "th" }, StringSplitOptions.None);
                        else
                            arr = val.Split(new string[] { "rd" }, StringSplitOptions.None);

                        if (arr.Length == 2)
                        {
                            int dd = Convert.ToInt32(arr[0]);
                            string mmyy = arr[1];
                            int mm;
                            int yy;
                            if (mmyy.Contains(","))
                            {
                                string[] arr1 = mmyy.Split(',');
                                if (arr1.Length == 2)
                                {
                                    if (int.TryParse(arr1[0], out mm))
                                    {
                                        mm = Convert.ToInt32(arr1[0]);
                                    }
                                    else
                                    {
                                        mm = getMonth(arr1[0]);
                                    }

                                    yy = Convert.ToInt32(arr1[1]);
                                    if (mm < 10)
                                        correcteddate = dd + "-0" + mm + "-" + yy;
                                    else
                                        correcteddate = dd + "-" + mm + "-" + yy;
                                }
                                else
                                {
                                    //error
                                    int er = 0;
                                }

                            }
                        }
                    }
                    else
                    {
                        string Error = "Invalid date format - The value read is # " + val + " #";
                        //ExcelErrorLog(ExcelID, Table_No, Row_no, Error);
                        correcteddate = val;
                    }
                }
            }
            return correcteddate;
        }
        private int getMonth(string t)
        {
            t = t.ToLower().Trim();
            int mm = 0;
            if (t.Equals("jan"))
                mm = 1;
            else if (t.Equals("feb"))
                mm = 2;
            else if (t.Equals("mar"))
                mm = 3;
            else if (t.Equals("apr"))
                mm = 4;
            else if (t.Equals("may"))
                mm = 5;
            else if (t.Equals("jun"))
                mm = 6;
            else if (t.Equals("jul"))
                mm = 7;
            else if (t.Equals("aug"))
                mm = 8;
            else if (t.Equals("sep"))
                mm = 9;
            else if (t.Equals("oct"))
                mm = 10;
            else if (t.Equals("nov"))
                mm = 11;
            else if (t.Equals("dec"))
                mm = 12;

            return mm;
        }

        private int getMonth1(string t)
        {
            t = t.ToLower().Trim();
            int mm = 0;
            if (t.Equals("january"))
                mm = 1;
            else if (t.Equals("feburary"))
                mm = 2;
            else if (t.Equals("march"))
                mm = 3;
            else if (t.Equals("april"))
                mm = 4;
            else if (t.Equals("may"))
                mm = 5;
            else if (t.Equals("june"))
                mm = 6;
            else if (t.Equals("july"))
                mm = 7;
            else if (t.Equals("august"))
                mm = 8;
            else if (t.Equals("september"))
                mm = 9;
            else if (t.Equals("october"))
                mm = 10;
            else if (t.Equals("november"))
                mm = 11;
            else if (t.Equals("december"))
                mm = 12;

            return mm;
        }
        private T GetColValOld<T>(object val,out bool isValidVal)
        {
            isValidVal = true;
            if (val == null || val == DBNull.Value || val.ToString()=="")
            {
                isValidVal = false;
                return default(T);
                
            }
            return (T)Convert.ChangeType(val, typeof(T));
        }
        private string? GetColStringVal(object val, out bool isValidVal)
        {
            isValidVal = true;
            string? retVal = null;
            if (val == null || val == DBNull.Value || val.ToString() == "")
            {
                isValidVal = false;
            }
            if (val.ToString() == "NA")
            {
                return null; 
            }
            return retVal;
        }
        private T? GetColVal<T>(object val, out bool isValidVal) where T : struct
        {
            isValidVal = true;

            if (val == null || val == DBNull.Value || val.ToString() == "")
            {
                isValidVal = false;
                return null;  // Return null for nullable value types
            }

            if (val.ToString() == "NA")
            {
                return null;  // Explicitly return null for "NA"
            }

            try
            {
                return (T)Convert.ChangeType(val, typeof(T));
            }
            catch
            {
                isValidVal = false;
                return null;  // Return null if conversion fails
            }
        }
        #endregion
    }
}
