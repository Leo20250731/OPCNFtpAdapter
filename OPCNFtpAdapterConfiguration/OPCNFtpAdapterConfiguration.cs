using Newtonsoft.Json;
using System.Collections.Generic;

namespace Siemens.MOM.Interoperability.Adapter
{
    [ConfigurationClass("FTPAdapter", "Configuration for FTP Adapter")]
    public class FTPAdapterConfiguration : ChannelAdapterConfiguration
    {
        /// <inheritdoc/>
        public override string AdapterConfigurationTypeName => nameof(FTPAdapterConfiguration);

        /// <inheritdoc/>
        public override string AdapterType { get; set; } = "FTPAdapter";

        /// <summary>
        /// The FTP Interval
        /// </summary>
        [ConfigurationProperty("The FTP Check interval.")]
        [JsonProperty(Order = 100)]
        public int FtpInterval { get; set; }

        /// <summary>
        /// The name of the Data Handler plugin
        /// </summary>
        [ConfigurationProperty("The FtpInboundUri")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate, Order = 120)]
        public string FtpInboundUri { get; set; }

        /// <summary>
        /// The name of the Data Handler plugin
        /// </summary>
        [ConfigurationProperty("The FtpOutboundUri")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate, Order = 130)]
        public string FtpOutboundUri { get; set; }

        /// <summary>
        /// Use FTP Passive mode
        /// </summary>
        [ConfigurationProperty("Use Passive Mode", Description = "Enable FTP passive mode for firewall traversal")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate, Order = 140)]
        public bool FtpUsePassiveMode { get; set; }

        /// <summary>
        /// FTP connection timeout (seconds)
        /// </summary>
        [ConfigurationProperty("FTP Timeout(sec)", Description = "FTP connect timeout in seconds")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate, Order = 150)]
        public int FtpTimeout { get; set; }
    }
}