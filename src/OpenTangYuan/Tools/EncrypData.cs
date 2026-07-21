
/*----------------------------------------------------------------
           // 文  件    名：    EncrypData.cs
           // 文件功能描述：    数据加密解密类
           // 创 建 日  期：    2008-01-17       
           // 创   建人   ：    庞海峰
 -----------------------------------------------------------------------*/
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WebApi.Tools
{
    /// <summary>
    /// 对称加密算法 
    /// 包含字符串加密和文件加密两种方式
    /// </summary>
  public  class EncrypData
    {
        private SymmetricAlgorithm EncryptionService;

        private string Key, IV;
        ///
        /// 对称加密类的构造函数
        ///
        public EncrypData()
        {
            EncryptionService = new RijndaelManaged();
            Key = "PHFLib_E"; // 任意字符串
            IV = "PHFLib_C";//任意字符串
        }


        ///
        /// 对称加密类的构造函数,实例化初始化Key,Iv值
        ///
        /// 密匙
        /// 向量IV
        public EncrypData(string key, string iv)
        {
            EncryptionService = new RijndaelManaged();//Rijndael 算法的托管版本,所有对称加密算法的所有实现必须从Rijndael继承。
            Key = key;
            IV = iv;
        }

        ///
        /// 写入或得到自定义的密匙KEY
        ///
        public string key
        {
            set
            {
                Key = value;
            }
            get
            {
                return Key;
            }
        }

        ///
        /// 写入或得到自定义的向量IV
        ///
        public string iv
        {
            set
            {
                IV = value;
            }
            get
            {
                return IV;
            }
        }

        ///
        /// 获得密钥
        ///
        /// 密钥
        private byte[] GetKey()
        {
            string sTemp = Key;
            EncryptionService.GenerateKey();//生成随即KEY
            byte[] bytTemp = EncryptionService.Key;//把获取的密匙付值给BYTTEMP
            int KeyLength = bytTemp.Length;//获取元素总数
            if (sTemp.Length > KeyLength)//判断变量KEY的长度是否大于元素的总数
                sTemp = sTemp.Substring(0, KeyLength);//如果TRUE,那么从字符开始的地方截取以元素总数为最大限的字符。
            else if (sTemp.Length < KeyLength)//判断变量KEY的长度是否小于元素的总数
                sTemp = sTemp.PadRight(KeyLength, ' ');//左对齐STEMP字符串，并以空格来填充，长度达到元素的总数。
            return ASCIIEncoding.ASCII.GetBytes(sTemp);//对 Unicode 字符数组中或 String 中指定范围的字符进行编码(单个7位)，并将结果存储在指定的字节数组中。
        }
        ///
        /// 获得初始向量IV
        ///
        /// 初试向量IV
        private byte[] GetIV()
        {
            string sTemp = IV;
            EncryptionService.GenerateIV();
            byte[] bytTemp = EncryptionService.IV;
            int IVLength = bytTemp.Length;
            if (sTemp.Length > IVLength)
                sTemp = sTemp.Substring(0, IVLength);
            else if (sTemp.Length < IVLength)
                sTemp = sTemp.PadRight(IVLength, ' ');
            return ASCIIEncoding.ASCII.GetBytes(sTemp);
        }
        ///
        /// 加密方法
        ///
        /// 待加密的串
        /// 经过加密的串
        public string EncrypString(string Source)
        {
            byte[] bytIn = UTF8Encoding.UTF8.GetBytes(Source);//对 Unicode 字符数组中或 String 中指定范围的字符进行编码(单个8位)，并将结果存储在指定的字节数组中，把结果付给字节数组。
            MemoryStream ms = new MemoryStream();//创建其支持存储区为内存的流。
            EncryptionService.Key = GetKey();//设置KEY的值.
            EncryptionService.IV = GetIV();//设置向量IV的值.
            ICryptoTransform encrypto = EncryptionService.CreateEncryptor();//创建基本的加密对象
            CryptoStream cs = new CryptoStream(ms, encrypto, CryptoStreamMode.Write);//将MS流转换为加密流,以encrypto对流进行加密转换。CryptoStreamMode模式为写访问。
            cs.Write(bytIn, 0, bytIn.Length);//将BYTIN字节数组全部写入加密流。
            cs.FlushFinalBlock();//用缓冲区的当前状态更新基础数据源或储存库，随后清除缓冲区。
            ms.Close();//关闭MS流。
            byte[] bytOut = ms.ToArray();//将整个流内容写入字节数组并付给BYTOUT字节数组。
            return Convert.ToBase64String(bytOut);//将单个为8位的字节数组转换为字符串，并64为基的数字组成。
        }
        ///
        /// 解密方法
        ///
        /// 待解密的串
        /// 经过解密的串
        public string DecrypString(string Source)
        {
            byte[] bytIn = Convert.FromBase64String(Source);//把一个得到的64位为基数字组成的字符串转换为8位数组并付给一个字节数组。
            MemoryStream ms = new MemoryStream(bytIn, 0, bytIn.Length);//把字节数组写入流。
            EncryptionService.Key = GetKey();//设置KEY的值
            EncryptionService.IV = GetIV();//设置向量IV的值
            ICryptoTransform encrypto = EncryptionService.CreateDecryptor();//创建基本的加密对象
            CryptoStream cs = new CryptoStream(ms, encrypto, CryptoStreamMode.Read);//将MS流转换为加密流,以encrypto对流进行加密转换。CryptoStreamMode模式为读访问。
            StreamReader sr = new StreamReader(cs);//从加密流中读取字符。
            return sr.ReadToEnd();//得到一个从加密流开始位置到结束的字符。
        }

        #region 加密文件
        /// <summary>
        /// 文件加密
        /// </summary>
        /// <param name="inFileName">需要加密的文件(文件的完整路径)</param>
        /// <param name="outFileName">加密后的文件(文件的完整路径)</param>
        /// <param name="sAlgorithm">对称算法实例</param>
        /// <returns>bool</returns>
        public bool EncryptFile(string InFileName, string OutFileName)
        {
            //将key和IV处理成8个字符
            //string Key = Key;//"12345678";
            //string IV = IV;//"12345678";
            Key = Key.Substring(0, 8);
            IV = IV.Substring(0, 8);
            SymmetricAlgorithm sAlgorithm;
            sAlgorithm = new DESCryptoServiceProvider();
            sAlgorithm.Key = Encoding.UTF8.GetBytes(Key);
            sAlgorithm.IV = Encoding.UTF8.GetBytes(IV);



            //将文件内容读取到字节数组
            FileStream inFileStream = new FileStream(InFileName, FileMode.Open, FileAccess.Read);
            byte[] sourceByte = new byte[inFileStream.Length];
            inFileStream.Read(sourceByte, 0, sourceByte.Length);
            inFileStream.Flush();
            inFileStream.Close();

            MemoryStream encryptStream = new MemoryStream();
            CryptoStream encStream = new CryptoStream(encryptStream, sAlgorithm.CreateEncryptor(), CryptoStreamMode.Write);
            try
            {
                //利用链接流加密源字节数组
                encStream.Write(sourceByte, 0, sourceByte.Length);
                encStream.FlushFinalBlock();

                //将字节数组信息写入指定文件
                FileStream outFileStream = new FileStream(OutFileName, FileMode.OpenOrCreate, FileAccess.Write);
                BinaryWriter bWriter = new BinaryWriter(outFileStream);
                bWriter.Write(encryptStream.ToArray());
                encryptStream.Flush();

                bWriter.Close();
                encryptStream.Close();
            }
            catch (Exception error)
            {
                throw (error);
            }
            finally
            {
                encryptStream.Close();
                encStream.Close();
            }
            return true;
        }      

        #endregion

        #region 解密文件
        /// <summary>
        /// 文件解密
        /// </summary>
        /// <param name="inFileName">需要解密的文件(文件的完整路径)</param>
        /// <param name="outFileName">解密后的文件(文件的完整路径)</param>
        /// <param name="sAlgorithm">对称算法实例</param>
        /// <returns>bool</returns>
        public bool DecryptFile(string InFileName, string OutFileName)
        {
            //将key和IV处理成8个字符
            //string Key = Key;// "12345678";
            //string IV = IV;// "12345678";
            Key = Key.Substring(0, 8);
            IV = IV.Substring(0, 8);
            SymmetricAlgorithm sAlgorithm;
            sAlgorithm = new DESCryptoServiceProvider();
            sAlgorithm.Key = Encoding.UTF8.GetBytes(Key);
            sAlgorithm.IV = Encoding.UTF8.GetBytes(IV);
            //读取被加密文件到字节数组
            FileStream encryptFileStream = new FileStream(InFileName, FileMode.Open, FileAccess.Read);
            byte[] encryptByte = new byte[encryptFileStream.Length];
            encryptFileStream.Read(encryptByte, 0, encryptByte.Length);
            encryptFileStream.Flush();
            encryptFileStream.Close();

            MemoryStream decryptStream = new MemoryStream();
            CryptoStream encStream = new CryptoStream(decryptStream, sAlgorithm.CreateDecryptor(), CryptoStreamMode.Write);
            try
            {
                encStream.Write(encryptByte, 0, encryptByte.Length);
                encStream.FlushFinalBlock();

                byte[] decryptByte = decryptStream.ToArray();
                FileStream decryptFileStream = new FileStream(OutFileName, FileMode.OpenOrCreate, FileAccess.Write);
                BinaryWriter bWriter = new BinaryWriter(decryptFileStream, Encoding.GetEncoding("GB18030"));
                bWriter.Write(decryptByte);
                decryptFileStream.Flush();

                bWriter.Close();
                decryptFileStream.Close();
            }
            catch (Exception error)
            {
                throw (error);
            }
            finally
            {
                decryptStream.Close();
                encStream.Close();
            }

            return true;
        }


        /// <summary>
        /// 文件解密(直接返回byte[])
        /// </summary>
        /// <param name="inFileName">需要解密的文件(文件的完整路径)</param>        
        /// <param name="sAlgorithm">对称算法实例</param>
        /// <returns>byte[]</returns>
      public byte[] DecryptFile(string InFileName)
        {
            //将key和IV处理成8个字符
            //string Key = Key;// "12345678";
            //string IV = IV;// "12345678";
            Key = Key.Substring(0, 8);
            IV = IV.Substring(0, 8);
            SymmetricAlgorithm sAlgorithm;
            sAlgorithm = new DESCryptoServiceProvider();
            sAlgorithm.Key = Encoding.UTF8.GetBytes(Key);
            sAlgorithm.IV = Encoding.UTF8.GetBytes(IV);
            //读取被加密文件到字节数组
            FileStream encryptFileStream = new FileStream(InFileName, FileMode.Open, FileAccess.Read);
            byte[] encryptByte = new byte[encryptFileStream.Length];
            encryptFileStream.Read(encryptByte, 0, encryptByte.Length);
            encryptFileStream.Flush();
            encryptFileStream.Close();

            MemoryStream decryptStream = new MemoryStream();
            CryptoStream encStream = new CryptoStream(decryptStream, sAlgorithm.CreateDecryptor(), CryptoStreamMode.Write);
            try
            {
                encStream.Write(encryptByte, 0, encryptByte.Length);
                encStream.FlushFinalBlock();
                byte[] decryptByte = decryptStream.ToArray();                
                
                return encryptByte;
            }
            catch 
            {
               // throw (error);
                return null;
            }
            finally
            {
                decryptStream.Close();
                encStream.Close();                
            }

            //return encryptByte;
        }
        #endregion
    }
}
