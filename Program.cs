using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.IO.Compression;

namespace _0ch_log
{
    class Reader
    {
        /// <summary>
        /// URLの情報
        /// </summary>
        public struct URLInfo
        {
            public string protocol;
            public string host;
            public string boardKey;
            public string threadKey;
        }

        /// <summary>
        /// 例外
        /// </summary>
        public class ReaderException : Exception
        {
            public ReaderException(string message) : base(message) { }
        }

        /// <summary>
        /// URLを取得しURLの情報を返す
        /// </summary>
        public static URLInfo NormalizeBoardURL(string url)
        {
            if (url.Substring(url.Length - 1) != "/")
            {
                url += "/";
            }
            URLInfo info = new URLInfo();
            
            try
            {
                // protocol
                info.protocol = url.Substring(0, url.IndexOf("://") + 3);

                // host
                int hostStart = url.IndexOf("://") + 3;
                int hostEnd = url.IndexOf("/", hostStart);
                info.host = url.Substring(hostStart, hostEnd - hostStart);

                // board key
                const string preBoardKey = "/test/read.cgi/";
                int boardKeyStart = url.IndexOf(preBoardKey, hostEnd) + preBoardKey.Length;
                int boardKeyEnd = url.IndexOf("/", boardKeyStart);
                info.boardKey = url.Substring(boardKeyStart, boardKeyEnd - boardKeyStart);

                // thread key
                int threadKeyStart = boardKeyEnd + 1;
                int threadKeyEnd = url.IndexOf("/", threadKeyStart);
                if (threadKeyEnd != -1)
                {
                    info.threadKey = url.Substring(threadKeyStart, threadKeyEnd - threadKeyStart);
                }
                else
                {
                    info.threadKey = "";
                }
            }
            catch (Exception)
            {
                throw new ReaderException("URLの正規化に失敗しました.");
            }

            return info;
        }

        /// <summary>
        /// subjectを取得する
        /// </summary>
        public static bool GetSubjectTxt(URLInfo info)
        {
            try
            {
                string path = CreateDirectory(info);

                string ifModSince = "";
                const string subjectHeaderPath = @"\subject.header";
                if (File.Exists(path + subjectHeaderPath))
                {
                    string str = File.ReadAllText(path + subjectHeaderPath, Encoding.ASCII);
                    const string lastMod = "Last-Modified: ";
                    int lastModStart = str.IndexOf(lastMod) + lastMod.Length;
                    int lastModEnd = str.IndexOf("\r\n", lastModStart);
                    ifModSince = "If-Modified-Since: " + str.Substring(lastModStart, lastModEnd - lastModStart) + "\r\n";
                }

                string rstr = "GET /" + info.boardKey + "/subject.txt HTTP/1.0\r\nAccept-Encoding: gzip\r\nHost: " + info.host + "\r\nUser-Agent: Monazilla/1.00\r\n" + ifModSince + "Connection: close\r\n\r\n";

                byte[] data = ReceiveData(10000, info.host, 80, rstr, 0x100);
                Console.Write("\n");

                const string message304 = "HTTP/1.1 304";
                int message304Length = Encoding.ASCII.GetBytes(message304).Length;
                if (Encoding.ASCII.GetString(data, 0, message304Length) == message304)
                {
                    return false; // 更新ナシ
                }

                const string message200 = "HTTP/1.1 200";
                int message200Length = Encoding.ASCII.GetBytes(message200).Length;
                if (Encoding.ASCII.GetString(data, 0, message200Length) != message200)
                {
                    throw new Exception();
                }

                string header = Encoding.ASCII.GetString(data, 0, 0x400);

                const string messageContentLength = "Content-Length: ";
                int contentLengthStart = header.IndexOf(messageContentLength);
                int contentLengthEnd = header.IndexOf("\r\n", contentLengthStart);
                int contentSize = int.Parse(header.Substring(contentLengthStart + messageContentLength.Length, contentLengthEnd - contentLengthStart - messageContentLength.Length));
                int contentStart = header.IndexOf("\r\n\r\n", contentLengthEnd) + 4;

                FileStream subjectHeader = new FileStream(path + subjectHeaderPath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                subjectHeader.Write(data, 0, contentStart);
                subjectHeader.Close();

                FileStream txt = new FileStream(path + @"\subject.txt", System.IO.FileMode.Create, System.IO.FileAccess.Write);
                txt.Write(data, contentStart, contentSize);
                txt.Close();
            }
            catch (Exception)
            {
                throw new ReaderException("スレッド一覧の取得に失敗しました.");
            }

            return true; // 受信成功, 更新アリ
        }

        /// <summary>
        /// datを取得する
        /// </summary>
        public static void GetDat(URLInfo info)
        {
            try
            {
                string path = CreateDirectory(info);
                string datPath = @"\" + info.threadKey + ".dat.raw";

                string ifModSince = "";
                string range = "";
                if (File.Exists(path + datPath))
                {
                    string str = File.ReadAllText(path + datPath, Encoding.ASCII);
                    
                    const string lastMod = "Last-Modified: ";
                    int lastModStart = str.IndexOf(lastMod) + lastMod.Length;
                    int lastModEnd = str.IndexOf("\r\n", lastModStart);
                    ifModSince = "If-Modified-Since: " + str.Substring(lastModStart, lastModEnd - lastModStart) + "\r\n";
                    
                    const string contentLength = "Content-Length: ";
                    int contentLengthStart = str.IndexOf(contentLength) + contentLength.Length;
                    int contentLengthEnd = str.IndexOf("\r\n", contentLengthStart);
                    range = "Range: bytes=" + str.Substring(contentLengthStart, contentLengthEnd - contentLengthStart) + "-\r\n";
                }

                string rstr = "GET /" + info.boardKey + "/dat/" + info.threadKey + ".dat HTTP/1.0\r\nAccept-Encoding: gzip\r\nHost: " + info.host + "\r\nUser-Agent: Monazilla/1.00\r\n" + ifModSince + range + "Connection: close\r\n\r\n";
                byte[] data = ReceiveData(10000, info.host, 80, rstr, 0x100);

                const string message206 = "HTTP/1.1 206";
                int message206Length = Encoding.ASCII.GetBytes(message206).Length;
                if (Encoding.ASCII.GetString(data, 0, message206Length) == message206)
                {
                    string str = Encoding.ASCII.GetString(data, 0, data.Length);

                    const string date = "Date: ";
                    int dateStart = str.IndexOf(date) + date.Length;
                    int dateEnd = str.IndexOf("\r\n", dateStart);

                    const string lastModified = "Last-Modified: ";
                    int lastModifiedStart = str.IndexOf(lastModified, dateEnd) + lastModified.Length;
                    int lastModifiedEnd = str.IndexOf("\r\n", lastModifiedStart);

                    const string contentLength = "Content-Length: ";
                    int contentLengthStart = str.IndexOf(contentLength, lastModifiedEnd) + contentLength.Length;
                    int contentLengthEnd = str.IndexOf("\r\n", contentLengthStart);
                    int contentLengthValue = int.Parse(str.Substring(contentLengthStart, contentLengthEnd - contentLengthStart));

                    int partialDatStart = str.IndexOf("\r\n\r\n", contentLengthEnd) + 4;

                    string dat = File.ReadAllText(path + datPath, Encoding.ASCII);

                    int datDateStart = dat.IndexOf(date) + date.Length;
                    int datDateEnd = dat.IndexOf("\r\n", datDateStart);

                    int datLastModifiedStart = dat.IndexOf(lastModified, datDateEnd) + lastModified.Length;
                    int datLastModifiedEnd = dat.IndexOf("\r\n", datLastModifiedStart);

                    int datContentLengthStart = dat.IndexOf(contentLength, datLastModifiedStart) + contentLength.Length;
                    int datContentLengthEnd = dat.IndexOf("\r\n", datContentLengthStart);
                    int datContentLengthValue = int.Parse(dat.Substring(datContentLengthStart, datContentLengthEnd - datContentLengthStart));

                    byte[] totalContentLength = Encoding.ASCII.GetBytes((contentLengthValue + datContentLengthValue).ToString());

                    byte[] localDatData;
                    {
                        FileStream localDatDataFile = new FileStream(path + datPath, FileMode.Open, FileAccess.Read);
                        localDatData = new byte[localDatDataFile.Length];
                        localDatDataFile.Read(localDatData, 0, localDatData.Length);
                        localDatDataFile.Close();
                    }
                    FileStream localDat = new FileStream(path + datPath, System.IO.FileMode.Create, System.IO.FileAccess.Write);

                    localDat.Write(localDatData, 0, datDateStart);
                    localDat.Write(data, dateStart, dateEnd - dateStart);

                    localDat.Write(localDatData, datDateEnd, datLastModifiedStart - datDateEnd);
                    localDat.Write(data, lastModifiedStart, lastModifiedEnd - lastModifiedStart);

                    localDat.Write(localDatData, datLastModifiedEnd, datContentLengthStart - datLastModifiedEnd);
                    localDat.Write(totalContentLength, 0, totalContentLength.Length);

                    localDat.Write(localDatData, datContentLengthEnd, localDatData.Length - datContentLengthEnd);

                    localDat.Write(data, partialDatStart, data.Length - partialDatStart);

                    return;
                }
                
                const string message304 = "HTTP/1.1 304";
                int message304Length = Encoding.ASCII.GetBytes(message304).Length;
                if (Encoding.ASCII.GetString(data, 0, message304Length) == message304)
                {
                    return;
                }

                const string message416 = "HTTP/1.1 416";
                int message416Length = Encoding.ASCII.GetBytes(message416).Length;
                if (Encoding.ASCII.GetString(data, 0, message416Length) == message416)
                {
                    Console.Write("警告 : 416検出. ログを退避します.\n");

                    byte[] backupData;
                    {
                        FileStream backupRead = new FileStream(path + datPath, FileMode.Open, FileAccess.Read);
                        backupData = new byte[backupRead.Length];
                        backupRead.Read(backupData, 0, backupData.Length);
                        backupRead.Close();
                    }
                    FileStream backup = new FileStream(path + datPath + "." + DateTime.UtcNow.ToString(), System.IO.FileMode.Create, System.IO.FileAccess.Write);
                    backup.Write(backupData, 0, backupData.Length);

                    File.Delete(path + datPath);
                    GetDat(info);

                    return;
                }

                const string message200 = "HTTP/1.1 200";
                int message200Length = Encoding.ASCII.GetBytes(message200).Length;
                if (Encoding.ASCII.GetString(data, 0, message200Length) != message200)
                {
                    throw new Exception();
                }
                
                FileStream fs = new FileStream(path + datPath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                fs.Write(data, 0, data.Length);
                fs.Close();
            }
            catch (Exception)
            {
                throw new ReaderException("datの取得に失敗しました.");
            }
        }

        /// <summary>
        /// 板のスナップショットを取得する
        /// </summary>
        public static void GetSnapshot(string url, int limit)
        {
            URLInfo info;
            URLInfo[] threadInfo;

            try
            {
                info = NormalizeBoardURL(url);
                Console.WriteLine("subject.txtの取得を開始します. ----");

                if (!GetSubjectTxt(info))
                {
                    Console.Write("\n");
                    Console.WriteLine("更新なし.");
                    Console.WriteLine("終了します.");
                    return;
                }

                string datPath = CreateDirectory(info);
                string[] lines = File.ReadAllLines(datPath + @"\subject.txt", Encoding.ASCII);

                threadInfo = new URLInfo[lines.Length];

                for (int i = 0; i < lines.Length; ++i)
                {
                    threadInfo[i] = info;
                    threadInfo[i].threadKey = lines[i].Substring(0, 10);
                }
            }
            catch (Exception e)
            {
                Console.Write("\n");
                Console.WriteLine(e.Message);
                Console.WriteLine("終了します.");
                return;
            }

            Console.Write("\n");

            Console.WriteLine("全スレッドの取得を開始します. ----");
            for (int i = 0; i < threadInfo.Length; ++i)
            {
                if (limit > 0 && i > limit)
                {
                    break;
                }

                try
                {
                    Console.WriteLine(threadInfo[i].host + " > " + threadInfo[i].boardKey + " > " + threadInfo[i].threadKey);
                    GetDat(threadInfo[i]);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
                
                Console.Write("\n");
            }

            Console.Write("\n");
            Console.WriteLine("完了. 終了します.");
        }

        static string CreateDirectory(URLInfo info)
        {
            string path = @"log\" + info.host + @"\" + info.boardKey;
            Directory.CreateDirectory(path);
            return path;
        }

        static byte[] ReceiveData(int timeout, string host, int port, string req, int bsize)
        {
            TcpClient tcp = new TcpClient();
            tcp.ReceiveTimeout = timeout;
            tcp.Connect(host, port);
            NetworkStream ns = tcp.GetStream();
            byte[] buff = Encoding.UTF8.GetBytes(req);
            ns.Write(buff, 0, buff.Length);
            ns.Flush();

            int progressSwitch = 0;
            int progressValue = 0;
            int contentLengthValue = 0;
            int receivedSize = 0;

            MemoryStream ms = new MemoryStream();
            byte[] data = new byte[bsize];
            do
            {
                int rs = ns.Read(data, 0, data.Length);
                if (rs == 0)
                {
                    break;
                }
                ms.Write(data, 0, rs);

                switch (progressSwitch)
                {
                    case 0:
                        {
                            const string contentLength = "Content-Length: ";
                            string str = Encoding.ASCII.GetString(ms.ToArray());
                            int contentLengthStart = str.IndexOf(contentLength);
                            if (contentLengthStart != -1)
                            {
                                int contentLengthEnd = str.IndexOf("\r\n", contentLengthStart + contentLength.Length);
                                if (contentLengthEnd != -1)
                                {
                                    contentLengthStart += contentLength.Length;
                                    contentLengthValue = int.Parse(str.Substring(contentLengthStart, contentLengthEnd - contentLengthStart));
                                    progressSwitch++;
                                }
                            }
                        }
                        break;

                    case 1:
                        {
                            string str = Encoding.ASCII.GetString(ms.ToArray());
                            int contentStart = str.IndexOf("\r\n\r\n");
                            if (contentStart != -1)
                            {
                                receivedSize = str.Length - (contentStart + 4);
                                progressSwitch++;
                            }
                        }
                        break;

                    case 2:
                        {
                            receivedSize += bsize;
                            double n = 10.0 * (double)receivedSize / (double)contentLengthValue;
                            if (n >= (double)(progressValue + 1))
                            {
                                Console.Write("{0}", progressValue);
                                progressValue++;
                            }
                        }
                        break;
                }
            } while(ns.CanRead);

            Console.Write("/");

            byte[] ret = ms.ToArray();
            ms.Close();
            ns.Close();
            tcp.Close();
            return ret;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 1)
            {
                Reader.GetSnapshot(args[0], 0);
            }
            return;
        }
    }
}
