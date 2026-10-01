using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Siemens.Camstar.Utility;

namespace Siemens.MOM.Interoperability
{
    /// <summary>
    ///     This Channel Adapter class derives from ChannelAdapterBase
    ///     (Which exposes IChannelAdapter).
    ///     This adapter is an example for reference
    /// 
    /// ****************************************************
    ///      This is for example purposes only!!
    ///
    /// Copyright © 2016 Siemens Product Lifecycle Management Software Inc.
    ///
    /// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, 
    /// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF 
    /// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
    /// IN NO EVENT SHALL SIEMENS BE LIABLE FOR ANY CLAIM, DAMAGES OR 
    /// OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, 
    /// ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE 
    /// OR OTHER DEALINGS IN THE SOFTWARE.
    //
    /// ****************************************************
    /// </summary>
    public partial class OPCNFtpAdapter
    {

        /// <summary>
        /// Handle sending a message in the adapter-specific manner
        /// </summary>
        /// <param name="sentMessage">the message to be sent</param>
        /// <param name="token"></param>

        protected override async Task<bool> OnSendMessage(CIOMessage sentMessage, CancellationToken token)
        {
            bool result;

            var en = Encoding.GetEncoding(EncodingName);

            var ftpRequest = (FtpWebRequest)WebRequest.Create(_ftpOutboundUri + "/" + sentMessage.Name);
            ftpRequest.Credentials = new NetworkCredential(UserName, Password);
            ftpRequest.UseBinary = true;
            ftpRequest.UsePassive = true;
            ftpRequest.KeepAlive = true;

            ftpRequest.Method = WebRequestMethods.Ftp.UploadFile;

            try
            {

                using (var ftpStream = ftpRequest.GetRequestStream())
                {
                    var bytes = en.GetBytes(sentMessage.Contents);
                    var outgoingPtr = 0;
                    var buffer = new byte[BufferSize];

                    while (outgoingPtr < bytes.Length)
                    {
                        int bytesToSend = Math.Min(buffer.Length, bytes.Length - outgoingPtr);

                        Buffer.BlockCopy(bytes, outgoingPtr, buffer, 0, bytesToSend);

                        outgoingPtr += bytesToSend;

                        // Transmit outbound buffer
                        if (bytesToSend > 0)
                        {
                            await ftpStream.WriteAsync(buffer, 0, bytesToSend, token);
                        }

                    }
                }

                _lastExceptionMessage = string.Empty;
                result = true;
            }
            catch (Exception ex)
            {
                var traceMsg =
                        string.Format("Adapter [{0}] Exception uploading Message {1}", Name,
                                      ex.Message);

                if (LogHandler != null)
                    LogHandler(traceMsg, Tracing.LogCategory.Error, Tracing.LogLevel.CriticalHigh, sentMessage);

                //if we failed call the appropriate base method
                result = base.SendMessageFailed();
            }
            return result;
        }
    }
}
