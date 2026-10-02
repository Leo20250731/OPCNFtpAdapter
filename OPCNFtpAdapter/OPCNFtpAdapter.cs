using System;
using System.ComponentModel.Composition;
using System.Text;
using System.Diagnostics;
using System.Threading;
using Siemens.Camstar.Utility;
using Siemens.MOM.Interoperability.Adapter;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Siemens.MOM.Interoperability
{
    /// <summary>
    ///     This Channel Adapter class derives from ChannelAdapterBase
    ///     (Which exposes IChannelAdapter).
    ///     This adapter connects to FTP Servers
    /// </summary>
    [PartCreationPolicy(CreationPolicy.NonShared)]
    [Export(typeof(IChannelAdapter))]
    [PluginMetadata("FTPAdapter")]
    public partial class OPCNFtpAdapter : CIOChannelAdapterBase
    {
        #region PrivateVariables

        private Timer _ftpConnectionTimer;
        private TimeSpan _ftpInterval;
        private ProcessingState _processingState;

        private readonly object _managingLock = new object();
        private readonly ReaderWriterLockSlim _stateLock;

        private volatile bool _requestStop;
        private string _ftpInboundUri;
        private string _ftpOutboundUri;

        private bool _ftpUsePassiveMode;
        private int _ftpTimeout;

        private const int BufferSize = 2048;
        private readonly TimeSpan _timeSpanInfinite = new TimeSpan(0, 0, 0, 0, -1);

        private string _lastExceptionMessage;

        // private int _ftpSendMetricId;

        private enum ProcessingState
        {
            Idle = 0,
            Running = 1
        }

        #endregion

        #region ctor

        public OPCNFtpAdapter()
        {
            _stateLock = Locks.GetLockInstance(LockRecursionPolicy.NoRecursion);
            _lastExceptionMessage = string.Empty;
            SupportsOutbound = true;
        }

        #endregion

        #region Adapter Method Overrides

        /// <summary>
        /// dispose of managed resources
        /// </summary>
        protected override void OnDisposeProcessing()
        {
            //dispose of the timer object
            if (_ftpConnectionTimer != null)
            {
                _ftpConnectionTimer.Dispose();
                _ftpConnectionTimer = null;
            }

        }

        /// <summary>
        /// handle deserialization of the Adapter-specific fields
        /// </summary>
        /// <param name="serializedConfig">serialized configuration xml</param>
        /// <param name="createMetricAction">function that can be used to create a CIOmetric specific to this adapter</param>
        /// <returns>informational message</returns>
        protected override string OnInitializeProcessing(ChannelAdapterConfiguration adapterConfiguration)
        {
            if (DiagnosticsMode && adapterConfiguration == null)
                return "Adapter is in Diagnostics mode and no configuration was provided";

            if (!DiagnosticsMode)
            {
                // 框架已经完成反序列化，直接强转为你的自定义配置类
                var ftpCfg = adapterConfiguration as FTPAdapterConfiguration;
                if (ftpCfg == null)
                {
                    throw new InvalidCastException($"[{Name}] Failed to cast configuration to FTPAdapterConfiguration");
                }

                // 参数校验
                if (string.IsNullOrWhiteSpace(ftpCfg.FtpInboundUri) && string.IsNullOrWhiteSpace(ftpCfg.FtpOutboundUri))
                {
                    throw new ArgumentException($"Adapter [{Name}]: Both FtpInboundUri and FtpOutboundUri cannot be empty.");
                }

                _ftpInboundUri = ftpCfg.FtpInboundUri;
                _ftpOutboundUri = ftpCfg.FtpOutboundUri;

                //轮询间隔，做下限保护，最小1秒
                _ftpInterval = ftpCfg.FtpInterval > 0
                    ? TimeSpan.FromSeconds(ftpCfg.FtpInterval)
                    : _timeSpanInfinite;

                _ftpUsePassiveMode = ftpCfg.FtpUsePassiveMode;
                _ftpTimeout = ftpCfg.FtpTimeout > 0 ? ftpCfg.FtpTimeout : 30;
            }

            return string.Empty;
        }


        /// <summary>
        /// start the timer that periodically invokes the ManageFTPConnection method
        /// </summary>
        /// <returns>optional connection message</returns>
        protected override string OnStartProcessing()
        {
            //reset the flag used to request that a stop
            _requestStop = false;

            //start the ftp connection timer
            _ftpConnectionTimer = new Timer(ManageFtpConnection, null, _ftpInterval, _timeSpanInfinite);

            return string.Empty;
        }

        /// <summary>
        /// Stop the timer that periodically invokes the ManageFTPConnection method and cleanup
        /// </summary>
        /// <returns>optional disconnection message</returns>
        protected override string OnStopProcessing()
        {
            _requestStop = true;

            //dispose of the timer object
            OnDisposeProcessing();

            //wait for the ManageFtpConnection to finish - this is running in a thread created by the timer
            DateTime whenToWaitUntil = DateTime.Now.Add(ShutdownMaxTime);
            while (!ThreadIsIdle() || DateTime.Now.CompareTo(whenToWaitUntil) > 0)
            {
                Thread.Sleep(20);
            }

            return string.Empty;
        }

        /// <summary>
        /// This method is called during periods of inactivity (as specified by
        /// the wake interal).  Handle any processing that might be needed
        /// </summary>
        protected override void OnWakeUpProcessing()
        {
            //do nothing 
        }

        /// <summary>
        ///  handle the message has been received 
        /// </summary>
        /// <param name="receivedMessage"></param>
        /// <param name="failure"></param>
        protected override void OnMessageReceived(CIOMessage receivedMessage, bool failure)
        {
            base.OnMessageReceived(receivedMessage, failure);
        }

        /// <summary>
        ///     When the Message has been added to the Message Broker,
        ///     mark it for deletion
        /// </summary>
        /// <param name="newMsgName">name of the message</param>
        /// <param name="failure"></param>
        protected override bool OnMessageSent(CIOMessage sentMessage, bool failure)
        {

            if (MessagesInProcess != null && MessagesInProcess.Count > 0)
            {

                //get filename associated with this message
                string filename = MessagesInProcess.FindKey(sentMessage.Name);

                if (!string.IsNullOrEmpty(filename))
                {

                    string msg = string.Format("Adapter [{0}] Message [{1}] Marked for Deletion", Name, filename);
                    Trace.WriteLineIf(Tracing.CamstarSwitch.TraceVerbose, Tracing.CurrentTime + msg);
                    MessagesInProcess.MarkSuccess(filename);
                }
            }
            else
            {
                string msg = MessagesInProcess == null ?
                    string.Format("Adapter [{0}] Message [{1}] to be Marked but Messages In Process Disposed", Name, sentMessage.Name)
                    : string.Format("Adapter [{0}] Message [{1}] to be Marked but no Messages In Process", Name, sentMessage.Name);
                Trace.WriteLineIf(Tracing.CamstarSwitch.TraceVerbose, Tracing.CurrentTime + msg);
            }
            return true;
        }
        #endregion

        #region Private Routines

        /// <summary>
        /// main routine called by the timer to periodically 
        /// "manage" the ftp connection by
        ///   checking for new files
        ///   downloading new files
        ///   deleting processed files
        /// </summary>
        /// <param name="state"></param>
        private void ManageFtpConnection(object state)
        {
            //check if this thread is already running
            if (Monitor.TryEnter(_managingLock))
            {

                try
                {
                    SetThreadToRunningState();

                    if (!_requestStop)
                    {
                        var filesToProcess = GetAvailableFilesOnServer();

                        if (filesToProcess.Any())
                            GetFilesFromServer(filesToProcess);

                        DeleteFilesFromServer();
                    }
                    //indicate that the inbound is online
                    SetInboundOnline();
                    _lastExceptionMessage = string.Empty;

                }
                catch (Exception ex)
                {
                    string msg =
                        string.Format("Adapter [{0}] Exception in ManageFtpConnection - {1}", Name,
                                      ex.Message);
                    Trace.WriteLineIf(Tracing.CamstarSwitch.TraceWarning, Tracing.CurrentTime + msg);

                    if (!InboundIsOffline)
                    {
                        //indicate that inbound is offline
                        SetInboundOffline();

                        if (msg != _lastExceptionMessage) //only log this if it is unique
                            RaiseAdapterEvent(msg, Tracing.LogLevel.CriticalHigh, Tracing.CategoryError);
                    }
                }
                finally
                {
                    //make sure the timer runs again
                    if ((_ftpConnectionTimer != null) && (!_requestStop))
                        _ftpConnectionTimer.Change(_ftpInterval, _timeSpanInfinite);

                    SetThreadToIdleState();

                    Monitor.Exit(_managingLock);
                }
            }
        }

        /// <summary>
        /// pull the available files from the server
        /// </summary>
        /// <returns></returns>
        private List<string> GetAvailableFilesOnServer()
        {

            var fileList = new List<string>();

            // Get the object used to communicate with the server.
            var ftpRequest = (FtpWebRequest)WebRequest.Create(_ftpInboundUri);
            ftpRequest.UseBinary = true;
            ftpRequest.UsePassive = _ftpUsePassiveMode;
            ftpRequest.KeepAlive = true;
            ftpRequest.Method = WebRequestMethods.Ftp.ListDirectory;
            ftpRequest.Credentials = new NetworkCredential(UserName, Password);
            ftpRequest.Timeout = _ftpTimeout * 1000;


            FtpWebResponse ftpResponse = null;
            try
            {
                ftpResponse = (FtpWebResponse)ftpRequest.GetResponse();

                using (Stream responseStream = ftpResponse.GetResponseStream())
                {
                    using (var reader = new StreamReader(responseStream))
                    {
                        while (reader.Peek() != -1)
                        {
                            var fileName = reader.ReadLine();
                            if (!string.IsNullOrEmpty(fileName))
                            {
                                if (!MessagesInProcess.Contains(fileName))
                                    fileList.Add(fileName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string msg =
                      string.Format("Adapter [{0}] Exception getting directory {1} - {2}", Name, _ftpInboundUri,
                                    ex.Message);
                Trace.WriteLineIf(Tracing.CamstarSwitch.TraceWarning, Tracing.CurrentTime + msg);

                RaiseAdapterEvent(msg, Tracing.LogLevel.CriticalHigh, Tracing.CategoryError);
            }
            finally
            {
                if (ftpResponse != null) ftpResponse.Close();
            }

            return fileList;
        }


        /// <summary>
        /// get all available files from the ftp server
        /// and turn them into new inbound messages
        /// </summary>
        /// <param name="filesToGet">list of available files</param>
        private void GetFilesFromServer(IEnumerable<string> filesToGet)
        {
            foreach (var fileName in filesToGet)
            {
                if (_requestStop) return;

                var messageContent = GetFileContentsFromServer(fileName);


                if (!string.IsNullOrEmpty(messageContent))
                {
                    var newMessage = base.CreateNewMessage(messageContent, fileName);

                    //add message the the inprocess collection
                    if (MessagesInProcess.AddMessage(fileName, newMessage))
                    {
                        base.RaiseNewMessageReceivedEvent(ref newMessage);
                        if (newMessage.StatusDescription != string.Empty)
                        {
                            string msg = string.Format("Adapter [{0}] Message [{1}] Status [{2}]", Name,
                                newMessage.Name, newMessage.StatusDescription);
                            Trace.WriteLine(Tracing.CurrentTime + msg);
                            base.RaiseAdapterEvent(msg, Tracing.LogLevel.DebugMedium, Tracing.CategoryInfo, newMessage);
                        }
                    }
                    else
                    {
                        string existsMsg = string.Format("Adapter [{0}] Message [{1}] already exists",
                            Name, newMessage.Name);
                        Trace.WriteLine(Tracing.CurrentTime + existsMsg);
                        base.RaiseAdapterEvent(existsMsg, Tracing.LogLevel.DebugMedium, Tracing.CategoryWarning, newMessage);
                    }
                }
            }
        }

        /// <summary>
        /// Handle Getting the contents of a remote file via FTP
        /// </summary>
        /// <param name="remoteFile">name of the file</param>
        /// <returns>contents of the file</returns>
        private string GetFileContentsFromServer(string remoteFile)
        {
            string messageContent = string.Empty;

            var en = Encoding.GetEncoding(EncodingName);

            var ftpRequest = (FtpWebRequest)WebRequest.Create(_ftpInboundUri + "/" + remoteFile);
            ftpRequest.Credentials = new NetworkCredential(UserName, Password);
            ftpRequest.UseBinary = true;
            ftpRequest.UsePassive = _ftpUsePassiveMode;
            ftpRequest.KeepAlive = true;
            ftpRequest.Timeout = _ftpTimeout * 1000;

            ftpRequest.Method = WebRequestMethods.Ftp.DownloadFile;
            FtpWebResponse ftpResponse = null;
            try
            {
                ftpResponse = (FtpWebResponse)ftpRequest.GetResponse();

                using (var responseStream = ftpResponse.GetResponseStream())
                {
                    var bytes = ReadFully(responseStream);

                    messageContent = GetStringFromStream(en, bytes);
                }
            }
            catch (Exception ex)
            {
                string msg =
                      string.Format("Adapter [{0}] Exception getting file {1}", Name,
                                    ex.Message);
                Trace.WriteLineIf(Tracing.CamstarSwitch.TraceWarning, Tracing.CurrentTime + msg);

                RaiseAdapterEvent(msg, Tracing.LogLevel.CriticalHigh, Tracing.CategoryError);
            }
            finally
            {
                if (ftpResponse != null) ftpResponse.Close();
            }


            return messageContent;

        }


        /// <summary>
        /// request that the file be deleted from the ftp server
        /// </summary>
        /// <param name="deleteFile">file to be deleted</param>
        private void DeleteFileFromServer(string deleteFile)
        {
            FtpWebResponse ftpResponse = null;
            var ftpRequest = (FtpWebRequest)WebRequest.Create(_ftpInboundUri + "/" + deleteFile);
            ftpRequest.Credentials = new NetworkCredential(UserName, Password);
            ftpRequest.UseBinary = true;
            ftpRequest.UsePassive = _ftpUsePassiveMode;
            ftpRequest.KeepAlive = true;
            ftpRequest.Method = WebRequestMethods.Ftp.DeleteFile;
            ftpRequest.Timeout = _ftpTimeout * 1000;

            try
            {

                ftpResponse = (FtpWebResponse)ftpRequest.GetResponse();

            }
            catch (Exception)
            {
                //catch (FileNotFoundException)
                //    {
                MessagesInProcess.RemoveKey(deleteFile);
                //}
            }
            finally
            {
                if (ftpResponse != null) ftpResponse.Close();
            }
        }

        /// <summary>
        ///     physically remove all files that
        ///     are marked to delete
        /// </summary>
        private void DeleteFilesFromServer()
        {
            if (MessagesInProcess != null && MessagesInProcess.Count > 0)
            {

                var filesToDelete = MessagesInProcess
                    .SuccessfulKeys.Concat(MessagesInProcess.FailedKeys)
                    .Concat(MessagesInProcess.ExpiredKeys(TimeSpanSinceLastInbound));

                foreach (var fileName in filesToDelete)
                {
                    if (_requestStop) return;

                    try
                    {
                        // delete the file
                        DeleteFileFromServer(fileName);

                        string msg = string.Format("Adapter [{0}] Deleting File {1}", Name, fileName);
                        Trace.WriteLineIf(Tracing.CamstarSwitch.TraceVerbose, Tracing.CurrentTime + msg);
                        //log this event
                        base.RaiseAdapterEvent(msg, Tracing.LogLevel.DebugLow, Tracing.CategoryInfo);

                    }
                    catch (Exception ex)
                    {
                        string exMsg =
                            string.Format("Adapter [{0}] Unable to Delete the processed file [{1}] Exception: {2}",
                                Name, fileName, ex.Message);
                        Trace.WriteLineIf(Tracing.CamstarSwitch.TraceError, Tracing.CurrentTime + exMsg);
                        RaiseAdapterEvent(exMsg, Tracing.LogLevel.CriticalLow, Tracing.CategoryWarning);
                    }
                    //remove this file from the in process collection
                    MessagesInProcess.RemoveKey(fileName);
                }
            }
        }

        /// <summary>
        /// Read the stream contents into a byte array
        /// </summary>
        /// <param name="input">input stream</param>
        /// <returns>byte array</returns>
        private static byte[] ReadFully(Stream input)
        {
            using (var ms = new MemoryStream())
            {
                input.CopyTo(ms);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// the thread is not stopping or running
        /// </summary>
        /// <returns></returns>
        private bool ThreadIsIdle()
        {
            using (new ReadLock(_stateLock))
            {
                return _processingState == ProcessingState.Idle;
            }
        }


        /// <summary>
        /// set the processing state to running
        /// </summary>
        private void SetThreadToRunningState()
        {
            using (new WriteLock(_stateLock))
            {
                _processingState = ProcessingState.Running;
            }
        }

        /// <summary>
        /// set the processing state to completed
        /// </summary>
        private void SetThreadToIdleState()
        {
            using (new WriteLock(_stateLock))
            {
                _processingState = ProcessingState.Idle;
            }
        }

        private string GetStringFromStream(Encoding en, byte[] bytes)
        {
            var preamble = en.GetPreamble();
            if (preamble.Where((p, i) => p != bytes[i]).Any())
            {
                return en.GetString(bytes.ToArray());
            }
            return en.GetString(bytes.Skip(preamble.Length).ToArray());
        }

        #endregion


    }
}
