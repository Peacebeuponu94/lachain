﻿using System.Collections.Generic;
using Phorkus.WebAssembly;

namespace Phorkus.Core.VM
{
    public class EnvExternalHandler : IExternalHandler
    {
        private const string EnvModule = "env";

        public static int Handler_Env_GetCallValue(int offset)
        {
            return 0;
        }

        public static int Handler_Env_Call(
            int callSignatureOffset, int inputLength, int inputOffset, int valueOffset, int returnValueOffset
        )
        {
            var frame = VirtualMachine.ExecutionFrames.Peek();
            return 0;
        }
        
        public IEnumerable<FunctionImport> GetFunctionImports()
        {
            return new[]
            {
                new FunctionImport(EnvModule, "getcallvalue", typeof(EthereumExternalHandler).GetMethod(nameof(Handler_Env_GetCallValue)))
            };
        }
    }
}