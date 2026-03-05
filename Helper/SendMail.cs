using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Web;
using System.IO;
using SignalTracker.Helper;
using SignalTracker.Models; 


namespace SignalTracker
{
    
    public class SendMail
    {        
        ApplicationDbContext db = null;
        //CommonFunction cf = null;
        public SendMail(ApplicationDbContext context)
        {
            db = context;
            //cf = new CommonFunction(context, httpContextAccessor);
        }        
        public bool send_mail(string message, string[] to, string[] bcc, string subject, byte[] bt_attachment, string attachment_name)
        {

            bool isSend = false;
            try
            {
            //    to = "baghel3349@gmail.com".Split(',');
            //    bcc = "regulatorydatabase.demo@gmail.com".Split(',');

                var Set_email = db.m_email_setting.Where(a => a.m_Status_ID == 1).FirstOrDefault();
                if (Set_email != null)
                {
                    string from = Set_email.UserName;
                    string from_password = Set_email.Password;
                    string str_body = "<html><meta name='viewport' content='width=device-width, initial-scale=1'>" +
                                       "<body><table style='width:100%;border: 0;border-radius: 7px;overflow: hidden;' Cellspacing='0px' Cellpadding='0px'> " +
                                       "<tr><td style='padding:10px;'>" + message + "</td></tr><tr><td style='padding:10px 10px 20px;'>" +
                                       "<b style='color:#16992b;'>Regards,<br/>Assistant Secretary,<br/>Forum of Regulators</b><br/><br/><b>P.S.: This is an automated email. Please do not reply to this email.</b><td><tr></body></html>";

                    SmtpClient smtpClient = new SmtpClient();
                    AlternateView avHtml = AlternateView.CreateAlternateViewFromString
                        (str_body, null, MediaTypeNames.Text.Html);


                    //create the mail message
                    MailMessage mail = new MailMessage();
                    //set the FROM address
                    mail.From = new MailAddress(from, "MouleForecast");
                    //set the RECIPIENTS
                    for (int i = 0; i < to.Length; i++)
                    {
                        if (to[i] == "")
                            continue;
                        else if (to[i] == null)
                            continue;

                        mail.To.Add(to[i]);
                    }
                    if (to.Length == 0)
                        mail.To.Add(Set_email.received_email_on);
                    if (bcc != null)
                    {
                        for (int i = 0; i < bcc.Length; i++)
                        {
                            if (bcc[i] == "")
                                continue;
                            else if (bcc[i] == null)
                                continue;

                            mail.Bcc.Add(bcc[i]);
                        }
                    }                  
                    mail.Subject = subject;
                    mail.AlternateViews.Add(avHtml);
                    if (bt_attachment != null)
                    {
                        mail.Attachments.Add(new Attachment(new MemoryStream(bt_attachment), attachment_name));
                    }

                    smtpClient.Host = Set_email.SMTPServer;//"""relay-hosting.secureserver.net";;
                    smtpClient.Port = Convert.ToInt32(Set_email.SMTPPort);//25;

                    smtpClient.EnableSsl = Set_email.SSLayer;//false;
                    smtpClient.UseDefaultCredentials = false;
                    smtpClient.Credentials = new NetworkCredential(from, from_password);
                    smtpClient.Send(mail);
                    isSend = true;
                }
            }
            catch (Exception ex)
            {
                var writelog = new Writelog(db);                
                writelog.write_exception_log(0, "SendMail", "send_mail", DateTime.Now, ex);
            }
            return isSend;
        }
    }
}