using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using WebApi.Tools;


namespace WebApi
{
    public class DbTools
    {
        static public void WriteTxtLog(string msg, string logPath)
        {
            try
            {
                FileStream fs;
                StreamWriter sw;
                string nowPath = logPath;
                if (!Directory.Exists(nowPath))
                {
                    Directory.CreateDirectory(nowPath);
                }
                string fileName = nowPath + DateTime.Now.Date.ToString("yyyyMMdd") + ".txt";
                fs = new FileStream(fileName, FileMode.OpenOrCreate | FileMode.Append);
                sw = new StreamWriter(fs, Encoding.Default);
                sw.Write(Environment.NewLine + DateTime.Now.ToShortTimeString() + msg);
                sw.Close();
                fs.Close();
            }
            catch (Exception exp)
            {

                //throw;
            }

        }

        static public string GetLocalIp()
        {
            return Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault<IPAddress>(a => a.AddressFamily.ToString().Equals("InterNetwork")).ToString();
        }      

        
    }
}