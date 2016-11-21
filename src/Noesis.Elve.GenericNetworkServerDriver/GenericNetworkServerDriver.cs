using CodecoreTechnologies.Elve.DriverFramework;
using CodecoreTechnologies.Elve.DriverFramework.Scripting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace NoesisLabs.Elve.GenericNetworkServer
{
    [Driver("Generic Network Server Driver", "The generic network server driver allows interfacing with devices connected to the computer network via TCP/IP where no driver yet exists. Incoming data is expected to be delimited by a string of characters to distinguish packets.", "Ryan Melena", "Communications", "", "network", DriverCommunicationPort.Network, DriverMultipleInstances.MultiplePerDriverService, 1, 0, DriverReleaseStages.Production, "N/A", "", null)]
    public class GenericNetworkServerDriver : Driver
    {
        [DriverEventArg("Data", "The received data.", typeof(ScriptString))]
        [DriverEvent("Received Data", "Occurs when the driver receives incoming data from the network connection.")]
        public DriverEvent ReceivedData;

        private string _delimiter = "\r\n";
        private bool _isAcceptingTcpClient;
        private TcpListener _listener;
        private string _newLineString = "\r\n";
        private int _port;
        private System.Timers.Timer _refreshTimer;
        private TcpClient _tcpClient;

        [DriverSetting("Delimiter", "Defaults to carriage-return linefeed ( \\r\\n ). This is the string that delimits incoming data packets.\r\nSpecial Characters:\r\n* \\r = Carriage-return  (ASCII character 13)\r\n\r\n* \\n : Linefeed  (ASCII character 10)\r\n\r\n* \\t : Tab  (ASCII character 9)", "\\r\\n", false)]
        public string Delimiter
        {
            set
            {
                this._delimiter = value.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t");
            }
        }

        [DriverSetting("New Line String", "Defaults to carriage-return linefeed ( \\r\\n ). This string is appended to the string passed to the SendLine() method.\r\nSpecial Characters:\r\n* \\r = Carriage-return  (ASCII character 13)\r\n\r\n* \\n : Linefeed  (ASCII character 10)\r\n\r\n* \\t : Tab  (ASCII character 9)", "\\r\\n", false)]
        public string NewLineString
        {
            set
            {
                this._newLineString = value.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t");
            }
        }

        [DriverSetting("Port", "The port to connect to.", 0.0, 2147483647.0, null, true)]
        public int Port
        {
            set
            {
                this._port = value;
            }
        }
        [ScriptObjectMethodParameter("Data", "The data to send.")]
        [ScriptObjectMethod("Send ASCII Data", "Sends data over the tcp network connection.", "Send {PARAM|0|text to send} to {NAME}.")]
        public void Send(ScriptString data)
        {
            this.Send(data.ToPrimitiveString());
        }

        public void Send(string data)
        {
            this.SendBytes(Encoding.ASCII.GetBytes(data));
        }

        [ScriptObjectMethod("Send Byte Data", "Sends byte data to the tcp network port.", "Send the hexidecimal data {PARAM|0|0C0F} as bytes to {NAME}.")]
        [ScriptObjectMethodParameter("HexData", "The byte data represented as a contiguous string of hexidecimal. Example: \"00FF0900\"")]
        public void SendBytes(ScriptString hexData)
        {
            this.SendBytes(this.hexStringToByteArray((string)hexData));
        }

        [ScriptObjectMethod("Send Byte Data", "Sends binary data to the tcp network port.")]
        [ScriptObjectMethodParameter("data", "The binary data to send.")]
        public void SendBytes(ScriptByteArray data)
        {
            this.SendBytes((byte[])data);
        }

        public void SendBytes(byte[] data)
        {
            if (this._tcpClient != null && this._tcpClient.Client != null)
            {
                try
                {
                    this._tcpClient.Client.Send(data);
                }
                catch (Exception ex)
                {
                    this.Logger.Error((object)"Failed sending message.", ex);
                    this.Disconnect();
                }
            }
            else
            {
                this.Logger.Warning((object)"Could not send message because no client was connected.");
            }
        }

        [ScriptObjectMethod("Send ASCII Data with line ending", "Sends data over the network connection followed by the new line string.", "Send {PARAM|0|text to send} to {NAME} followed by line ending character(s).")]
        [ScriptObjectMethodParameter("Data", "The data to send.")]
        public void SendLine(ScriptString data)
        {
            this.Send(data.ToPrimitiveString() + this._newLineString);
        }

        public override bool StartDriver(Dictionary<string, byte[]> configFileData)
        {
            this._listener = new TcpListener(IPAddress.Any, this._port);

            this._listener.Start();

            this._refreshTimer = new System.Timers.Timer();
            this._refreshTimer.Interval = TimeSpan.FromSeconds(5).TotalMilliseconds;
            this._refreshTimer.AutoReset = true;
            this._refreshTimer.Elapsed += RefreshTimer_Elapsed;

            this.RefreshTimer_Elapsed(this, null);

            this._refreshTimer.Start();

            return false;
        }

        public override void StopDriver()
        {
            this.close();
        }

        private void close()
        {
            if (this._refreshTimer != null)
            {
                this._refreshTimer.Stop();
                this._refreshTimer = null;
            }

            if (this._listener != null)
            {
                this._listener.Stop();
                this._listener = null;
            }

            if (this._tcpClient != null)
            {
                this._tcpClient.Close();
                this._tcpClient = null;
            }
        }

        private void DoAcceptTcpClient(IAsyncResult ar)
        {
            try
            {
                // Get the listener that handles the client request.
                TcpListener listener = (TcpListener)ar.AsyncState;

                // End the operation and display the received data on the console.
                this._tcpClient = listener.EndAcceptTcpClient(ar);

                this.Logger.Debug((object)("Successfully connected to '" + this._tcpClient.Client.RemoteEndPoint.ToString() + "."));

                this._isAcceptingTcpClient = false;

                this.IsReady = true;

                using (NetworkStream ns = this._tcpClient.GetStream())
                using (StreamReader sr = new StreamReader(ns))
                {
                    while (this._tcpClient != null && this._tcpClient.Connected && this._tcpClient.Client != null && this._tcpClient.Client.IsConnected())
                    {
                        if (ns.DataAvailable)
                        {
                            string msg = sr.ReadToDelimiter(this._delimiter, this.Logger);
                            try
                            {
                                this.Logger.Debug((object)("Received: " + msg));
                                this.RaiseDeviceEvent(this.ReceivedData, new DriverEventArgDictionary()
                            {
                              {
                                "Data",
                                (IScriptObject) new ScriptString(msg)
                              }
                            });
                            }
                            catch (SocketException ex)
                            {
                                if (ex.ErrorCode == 10004) { continue; }
                                this.Logger.Error((object)("A socket error ocurred in the received response.  Error Code: " + ex.ErrorCode + "."), ex);
                                this.Disconnect();
                                break;
                            }
                            catch (Exception ex)
                            {
                                this.Logger.Error((object)"An error ocurred in the received response.", ex);
                                this.Disconnect();
                                break;
                            }
                        }
                        else
                        {
                            Thread.Sleep(100);
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                this.Logger.Error((object)("An error ocurred in DoAcceptTcpClient."), ex);
            }
            finally
            {
                this.Disconnect();
            }
        }

        private void Disconnect()
        {
            this.Logger.Warning((object)("Client disconnected, listening for new connection."));

            if (this._tcpClient != null && this._tcpClient.Client != null)
            {
                try
                {
                    this._tcpClient.Client.Disconnect(true);
                }
                catch { }
            }
        }

        private byte[] hexStringToByteArray(string hexData)
        {
            try
            {
                hexData = hexData.Replace(" ", "");
                byte[] numArray = new byte[hexData.Length / 2];
                int startIndex = 0;
                while (startIndex < hexData.Length)
                {
                    numArray[startIndex / 2] = Convert.ToByte(hexData.Substring(startIndex, 2), 16);
                    startIndex += 2;
                }
                return numArray;
            }
            catch (Exception ex)
            {
                throw new ArgumentException("The hexData string must contain contiguous 2 character hexidecimal values.", ex);
            }
        }

        private void RefreshTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if ((this._tcpClient == null || !this._tcpClient.Connected || this._tcpClient.Client == null || !this._tcpClient.Client.Connected) && !this._isAcceptingTcpClient)
            {
                this._isAcceptingTcpClient = true;

                this.IsReady = false;

                this.Logger.Debug((object)("Waiting for a connection."));

                this._listener.BeginAcceptTcpClient(new AsyncCallback(DoAcceptTcpClient), this._listener);
            }
        }
    }
}