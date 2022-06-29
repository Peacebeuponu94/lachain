﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AustinHarris.JsonRpc;
using Newtonsoft.Json.Linq;
using Lachain.Logger;
using Lachain.Utility.Utils;
using Secp256k1Net;

namespace Lachain.Core.RPC.HTTP
{
    public class HttpService
    {
        private static readonly ILogger<HttpService> Logger =
            LoggerFactory.GetLoggerForClass<HttpService>();

        public void Start(RpcConfig rpcConfig)
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    _Worker(rpcConfig);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e);
                }
            }, TaskCreationOptions.LongRunning);
        }

        private HttpListener? _httpListener;
        private string? _apiKey;

        private readonly List<string> _privateMethods = new List<string>
        {
            "validator_start",
            // TODO: remove this comment after the working auth
            //"validator_stop",
            "fe_sendTransaction",
            "deleteTransactionPoolRepository",
            "clearInMemoryPool",
            "eth_sendTransaction",
            "eth_signTransaction",
        };

        public void Stop()
        {
            _httpListener?.Stop();
        }

        private void _Worker(RpcConfig rpcConfig)
        {
            if(!HttpListener.IsSupported)
                throw new Exception("Your platform doesn't support [HttpListener]");
            _httpListener = new HttpListener();
            foreach(var host in rpcConfig.Hosts ?? throw new InvalidOperationException())
                _httpListener.Prefixes.Add($"http://{host}:{rpcConfig.Port}/");
            _httpListener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
            _apiKey = rpcConfig.ApiKey ?? throw new InvalidOperationException();
            _httpListener.Start();
            while(_httpListener.IsListening)
            {
                try
                {
                    _Handle(_httpListener.GetContext());
                }
                catch(Exception e)
                {
                    Console.Error.WriteLine(e);
                }
            }

            _httpListener.Stop();
        }

        private bool _Handle(HttpListenerContext context)
        {
            var request = context.Request;

            // Get signature from request header
            var signatures = request.Headers.GetValues("Signature");
            var signature = string.Empty;
            if(signatures != null && signatures.Length > 0)
            {
                signature = signatures[0];
            }

            // Get timestamp from request header
            var timestamps = request.Headers.GetValues("Timestamp");
            var timestamp = string.Empty;
            if(timestamps != null && timestamps.Length > 0)
            {
                timestamp = timestamps[0];
            }

            Logger.LogInformation($"{request.HttpMethod}");

            var response = context.Response;
            /* check is request options pre-flight */
            if(request.HttpMethod == "OPTIONS")
            {
                if(request.Headers["Origin"] != null)
                {
                    response.AddHeader("Access-Control-Allow-Origin", request.Headers["Origin"]!);
                }
                response.AddHeader("Access-Control-Allow-Headers", "*");
                response.AddHeader("Access-Control-Allow-Methods", "GET, POST");
                response.AddHeader("Access-Control-Max-Age", "1728000");
                response.AddHeader("Access-Control-Allow-Credentials", "true");
                response.Close();
                return true;
            }

            using var reader = new StreamReader(request.InputStream);
            var body = reader.ReadToEnd();
            Logger.LogInformation($"Body: [{body}]");
            var isArray = body.StartsWith("[");

            if(request.Headers["Origin"] != null)
                response.AddHeader("Access-Control-Allow-Origin", request.Headers["Origin"]!);
            response.Headers.Add("Access-Control-Allow-Methods", "POST, GET");
            response.Headers.Add("Access-Control-Allow-Credentials", "true");

            var rpcResultHandler = new AsyncCallback(result =>
            {
                if(!(result is JsonRpcStateAsync jsonRpcStateAsync))
                    return;

                var resultString = jsonRpcStateAsync.Result;

                if(isArray && !jsonRpcStateAsync.Result.StartsWith("["))
                    resultString = "[" + resultString + "]";

                var output = Encoding.UTF8.GetBytes(resultString);
                Logger.LogInformation($"output: [{resultString}]");
                response.OutputStream.Write(output, 0, output.Length);
                response.OutputStream.Flush();
                response.Close();
            });

            var async = new JsonRpcStateAsync(rpcResultHandler, null)
            {
                JsonRpc = body
            };

            var requests = isArray ? JArray.Parse(body) : new JArray { JObject.Parse(body) };
            foreach(var item in requests)
            {
                var requestObj = (JObject)item;
                if(requestObj == null) return false;

                if(!_CheckAuth(requestObj, context, signature, timestamp))
                {
                    var error = new JObject
                    {
                        ["code"] = -32600,
                        ["message"] = "Invalid API key",
                    };
                    var res = new JObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["error"] = error,
                        ["id"] = ulong.Parse(requestObj["id"]!.ToString()),
                    };
                    var output = Encoding.UTF8.GetBytes(res.ToString());
                    Logger.LogInformation($"output: [{res}]");
                    response.OutputStream.Write(output, 0, output.Length);
                    response.OutputStream.Flush();
                    response.Close();
                    return false;
                }
            }

            JsonRpcProcessor.Process(async);
            return true;
        }

        private string SerializeParams(JObject? args)
        {
            if (args is null)
                return "";
            
            string serializedParams = string.Empty;
            
            foreach(var param in args)
            {
                serializedParams += param.Key;
                if (param.Value is null)
                    continue;
                serializedParams += (param.Value.HasValues ? SerializeParams((JObject)param.Value) : param.Value.ToString());
            }

            return serializedParams;
        }

        private bool _CheckAuth(JObject body, HttpListenerContext context, string signature, string timestamp)
        {
            try
            {
                if(context.Request.IsLocal) return true;
                
                if(_privateMethods.Contains(body["method"]!.ToString()))
                {
                    if(string.IsNullOrEmpty(signature)) return false;
                    if(string.IsNullOrEmpty(timestamp)) return false;
                    // If unix timestamp diff is longer than 30 minutes, we dont handle it
                    if(!long.TryParse(timestamp.Trim(), out long unixTimestamp)) return false;
                    TimeSpan timeSpan = DateTimeOffset.Now.Subtract(DateTimeOffset.FromUnixTimeSeconds(unixTimestamp));
                    if(timeSpan.TotalMinutes >= 30)
                        return false;

                    // Serialize params (format: key1value1key2value2...)
                    string serializedParams = SerializeParams(body["params"] as JObject);

                    var messageToSign = body["method"]!.ToString() + serializedParams + timestamp;
                    var messageBytes = Encoding.UTF8.GetBytes(messageToSign);
                    var messageHash = System.Security.Cryptography.SHA256.Create().ComputeHash(messageBytes);

                    var signatureBytes = signature.HexToBytes();
                    var publicKey = _apiKey!.HexToBytes();

                    var secp256K1 = new Secp256k1();
                    return secp256K1.Verify(signatureBytes, messageHash, publicKey);
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning($"RPC auth error: {e}");
                return false;
            }

            return true;
        }
    }
}