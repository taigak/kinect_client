using System;
using System.Configuration;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using Microsoft.Kinect;
using System.Net;
using System.Threading;
using System.Net.NetworkInformation;
using Codeplex.Data;
using System.Collections.Generic;

namespace BodyIndexData
{
    class ConsoleApplication1
    {
        //Kinect SDK
        static KinectSensor kinect;
        static BodyFrameReader bodyFrameReader;

        static Body[] bodies;
        static NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
        static PhysicalAddress phy;
        static String channel;
        static int bodyCount;

        // 送信用
        static String JSON;

        static String ip = "localhost";
        static int port = 8000;
        static TcpClient tcpClient;
        static NetworkStream ns;
        static Byte[] StreamData;

        // Slack用設定
        static String slackUrl;
        static WebClient wc;

        static void Main(string[] args)
        {
            try
            {
                //slack設定
                slackUrl = kinect_client.Properties.Settings.Default.SlackURL.ToString();

                //channel識別用
                phy = adapters[0].GetPhysicalAddress();
                channel = phy.ToString();

                //Kinectを開く
                kinect = KinectSensor.GetDefault();
                kinect.Open();

                //BodyReaderを開く
                bodyFrameReader = kinect.BodyFrameSource.OpenReader();
                bodyFrameReader.FrameArrived += bodyFrameReader_FrameArrived;

                //Bodyの配列確保
                bodies = new Body[kinect.BodyFrameSource.BodyCount];


                //通信用ストリーム取得
                tcpClient = new TcpClient(ip, port);
                ns = tcpClient.GetStream();

                while (true)
                {
                    //適当にループ回す
                    //Frame届くたびbodyFrameReader_FrameArrivedに飛ぶ
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                PostSlack("Error Happened When Get Data from Kinect");
                stop();
            }

            //終了処理
            stop();
        }

        private static void bodyFrameReader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            try
            {

                UpdateBodyFrame(e);
                CreateJSON();
                SendJSON();

                Console.WriteLine(JSON);
                Console.WriteLine("-----------------------------------------------");
                Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void UpdateBodyFrame(BodyFrameArrivedEventArgs e)
        {

            using (var bodyFrame = e.FrameReference.AcquireFrame())
            {
                if (bodyFrame == null)
                {
                    Console.WriteLine("BodyFrame==null");
                    return;
                }

                //データ取得
                bodyFrame.GetAndRefreshBodyData(bodies);
            }
        }

        private static void CreateJSON()
        {
            // 組み立て
            var bodiesData = new List<List<Array>>();
            var jointsData = new List<Array>();
            foreach (var body in bodies.Where(b => b.IsTracked))
            {
                foreach (var joint in body.Joints)
                {
                    String[] jointData = {
                        joint.Value.JointType.ToString(),
                        joint.Value.Position.X.ToString(),
                        joint.Value.Position.Y.ToString(),
                        joint.Value.Position.Z.ToString(),
                    };
                    jointsData.Add(jointData);
                }
                bodiesData.Add(jointsData);
            }

            dynamic root = new DynamicJson();
            root.channel = channel;
            root.Time = DateTime.Now.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
            root.RecognizedBodyCount = GetBodyCount();
            root.bodies = bodiesData;
            JSON = root.ToString();

            //送信時の末尾判定用
            JSON = JSON + "\r\n";
        }

        private static void SendJSON()
        {
            //送信用に変換
            StreamData = Encoding.UTF8.GetBytes(JSON);
            try
            {
                //送信
                ns.Write(StreamData, 0, StreamData.Length);
                ns.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                PostSlack("Error Happened When Write Data to the Net");
                stop();
            }
        }

        private static int GetBodyCount()
        {
            bodyCount = 0;

            foreach (var body in bodies)
            {
                if (body.IsTracked == true)
                {
                    bodyCount++;
                }
            }

            return bodyCount;
        }

        private static void PostSlack(String msg)
        {
            wc = new WebClient();

            var data = DynamicJson.Serialize(new
            {
                text = "Channel=" + channel + ": " + msg
            });

            wc.Headers.Add(HttpRequestHeader.ContentType, "application/json;charset=UTF-8");
            wc.Encoding = Encoding.UTF8;
            wc.UploadString(slackUrl, data);

        }

        private static void stop()
        {
            Console.WriteLine("終了");
            if (ns != null) ns.Close();
            if (tcpClient != null) tcpClient.Close();
            wc.Dispose();
            Environment.Exit(0);
        }

    }

}