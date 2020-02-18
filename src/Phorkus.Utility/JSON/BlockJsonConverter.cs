﻿using System.Linq;
using Newtonsoft.Json.Linq;
using Phorkus.Proto;
using Phorkus.Utility.Utils;

namespace Phorkus.Utility.JSON
{
    public static class BlockJsonConverter
    {
        public static JObject ToJson(this BlockHeader blockHeader)
        {
            var json = new JObject
            {
                ["prevBlockHash"] = blockHeader.PrevBlockHash.Buffer.ToHex(),
                ["merkleRoot"] = blockHeader.MerkleRoot.Buffer.ToHex(),
                ["stateHash"] = blockHeader.StateHash.Buffer.ToHex(),
                ["index"] = blockHeader.Index,
                ["nonce"] = blockHeader.Index
            };
            return json;
        }

        public static JObject ToJson(this Block block)
        {
            var json = new JObject
            {
                ["header"] = block.Header.ToJson(),
                ["hash"] = block.Hash.Buffer.ToHex(),
                ["transactionHashes"] = new JArray(block.TransactionHashes.Select(txHash => txHash.Buffer.ToHex())),
                ["multisig"] = null,
                ["gasPrice"] = block.GasPrice,
                ["timestamp"] = block.Timestamp,
            };
            return json;
        }
    }
}