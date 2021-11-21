using System;
using System.Collections.Generic;
using System.Linq;
using Npgsql;

namespace CirclesLand.BlockchainIndexer
{
    public static class Settings
    {
        public static readonly string ConnectionString;
        public static readonly string RpcEndpointUrl;
        public static readonly string WebsocketServerUrl;

        public static readonly int UseBulkSourceThreshold;
        /// <summary>
        /// Specifies after how many imported blocks the import_from_staging_tables() procedure should be called.
        /// </summary>
        /// <remarks></remarks>
        public static readonly int BulkFlushInterval;
        public static readonly int BulkFlushTimeoutInSeconds;
        public static readonly int SerialFlushInterval;
        public static readonly int SerialFlushTimeoutInSeconds;

        public static readonly int ErrorRestartPenaltyInMs;
        public static readonly int MaxErrorRestartPenaltyInMs;
        public static readonly int PollingIntervalInMs;

        public static readonly int MaxParallelBlockDownloads;
        public static readonly int MaxDownloadedBlockBufferSize;

        public static readonly int MaxParallelReceiptDownloads;
        public static readonly int MaxDownloadedTransactionsBufferSize;
        public static readonly int MaxDownloadedReceiptsBufferSize;

        public static readonly int WriteToStagingBatchSize;
        public static readonly int WriteToStagingBatchMaxIntervalInSeconds;
        public static readonly int MaxWriteToStagingBatchBufferSize;

        private static readonly Dictionary<string, string> InvalidSettings = new();
        private static readonly Dictionary<string, (string, bool)> ValidSettings = new();
        
        static int TryGetIntEnvVar(string variableName, int defaultValue)
        {
            var val = Environment.GetEnvironmentVariable(variableName);
            var isInt = int.TryParse(val?.Trim(), out var i);
            if (val != null && !isInt)
            {
                InvalidSettings.Add(variableName, val ?? "null");
            }
            var returnVal = isInt ? i : defaultValue;
            ValidSettings.Add(variableName, (returnVal.ToString(), val == null));
            return returnVal;
        }
        
        static Settings()
        {
            var validationErrors = new List<string>();
            var connectionString = Environment.GetEnvironmentVariable("INDEXER_CONNECTION_STRING");
            try
            {
                var csb = new NpgsqlConnectionStringBuilder(connectionString);
                if (string.IsNullOrWhiteSpace(csb.Host))
                    validationErrors.Add("The INDEXER_CONNECTION_STRING contains no 'Server'");
                if (string.IsNullOrWhiteSpace(csb.Username))
                    validationErrors.Add("The INDEXER_CONNECTION_STRING contains no 'User ID'");
                if (string.IsNullOrWhiteSpace(csb.Database))
                    validationErrors.Add("The INDEXER_CONNECTION_STRING contains no 'Database'");
            }
            catch (Exception ex)
            {
                validationErrors.Add("The INDEXER_CONNECTION_STRING is not valid:");
                validationErrors.Add(ex.Message);
            }

            if (!Uri.TryCreate(Environment.GetEnvironmentVariable("INDEXER_RPC_GATEWAY_URL"), UriKind.Absolute, 
                out var rpcGatewayUri))
            {
                validationErrors.Add("Couldn't parse the 'INDEXER_RPC_GATEWAY_URL' environment variable. Expected 'System.Uri'.");
            }

            if (!Uri.TryCreate(Environment.GetEnvironmentVariable("INDEXER_WEBSOCKET_URL"), UriKind.Absolute,
                out var websocketUrl))
            {
                validationErrors.Add("Couldn't parse the 'INDEXER_WEBSOCKET_URL' environment variable. Expected 'System.Uri'.");
            }

            if (validationErrors.Count > 0)
            {
                throw new ArgumentException(string.Join(Environment.NewLine, validationErrors));
            }

            ConnectionString = connectionString ?? "null";
            ValidSettings.Add("INDEXER_CONNECTION_STRING", ("hidden", false));
            
            RpcEndpointUrl = rpcGatewayUri?.ToString() ?? "null";
            ValidSettings.Add("INDEXER_RPC_GATEWAY_URL", (RpcEndpointUrl, false));
            
            WebsocketServerUrl = websocketUrl?.ToString() ?? "null";
            ValidSettings.Add("INDEXER_WEBSOCKET_URL", (WebsocketServerUrl, false));
            
            UseBulkSourceThreshold = TryGetIntEnvVar("USE_BULK_SOURCE_THRESHOLD", 80);
            BulkFlushInterval = TryGetIntEnvVar("BULK_FLUSH_INTERVAL_IN_BLOCKS", 10);
            BulkFlushTimeoutInSeconds = TryGetIntEnvVar("BULK_FLUSH_TIMEOUT_IN_SECONDS", 240);
            SerialFlushInterval = TryGetIntEnvVar("SERIAL_FLUSH_INTERVAL_IN_BLOCKS",1 );
            SerialFlushTimeoutInSeconds = TryGetIntEnvVar("SERIAL_FLUSH_TIMEOUT_IN_SECONDS", 10);
            ErrorRestartPenaltyInMs = TryGetIntEnvVar("ERROR_RESTART_PENALTY_IN_MILLISECONDS", 1000 * 5);
            MaxErrorRestartPenaltyInMs = TryGetIntEnvVar("MAX_ERROR_RESTART_PENALTY_IN_MILLISECONDS", 1000 * 60 * 4);
            PollingIntervalInMs = TryGetIntEnvVar("POLLING_INTERVAL_IN_MILLISECONDS", 500);
            MaxParallelBlockDownloads = TryGetIntEnvVar("MAX_PARALLEL_BLOCK_DOWNLOADS", 24);
            MaxDownloadedBlockBufferSize = TryGetIntEnvVar("MAX_BLOCK_BUFFER_SIZE", 24 * 25);
            MaxParallelReceiptDownloads = TryGetIntEnvVar("MAX_PARALLEL_RECEIPT_DOWNLOADS", 96);
            MaxDownloadedTransactionsBufferSize = TryGetIntEnvVar("MAX_TRANSACTION_BUFFER_SIZE", 96 * 25);
            MaxDownloadedReceiptsBufferSize = TryGetIntEnvVar("MAX_RECEIPT_BUFFER_SIZE", 96 * 25);
            WriteToStagingBatchSize = TryGetIntEnvVar("WRITE_TO_STAGING_BATCH_SIZE", 2000);
            WriteToStagingBatchMaxIntervalInSeconds = TryGetIntEnvVar("WRITE_TO_STAGING_BATCH_MAX_INTERVAL_IN_SECONDS", 5);
            MaxWriteToStagingBatchBufferSize = TryGetIntEnvVar("MAX_WRITE_TO_STAGING_BATCH_BUFFER_SIZE", 25);

            Console.WriteLine("Configuration: ");
            Console.WriteLine("-------------------------------------------");
            foreach (var (key, value) in InvalidSettings)
            {
                Console.WriteLine($"ERR: The value of environment variable '{key}' is invalid (not an integer): {value}");
            }

            if (InvalidSettings.Count > 0)
            {
                Console.WriteLine("-------------------------------------------");
                throw new Exception("Invalid configuration");
            }
            
            var col1Width =  ValidSettings.Select(o => o.Key).Max(o => o.Length) + 2;
            var col2Width =  ValidSettings.Select(o => o.Value.Item1).Max(o => o.Length) + 2;
            
            foreach (var (key, value) in ValidSettings)
            {
                var formattedKey = (key + ": ").PadRight(col1Width);
                var formattedVal = value.Item1.PadRight(col2Width);
                var defaultIndicator = value.Item2 ? "(default)" : "(environment variable)";
                
                Console.WriteLine($"{formattedKey}{formattedVal} {defaultIndicator}");
            }
            
            Console.WriteLine("-------------------------------------------");
        }
    }
}