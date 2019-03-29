using System;
using System.Net;
using Mono.Debugging.Soft;
using SyntaxTree.VisualStudio.Unity.Messaging;

namespace MonoDevelop.Debugger.Soft.Unity
{
    public class UnityDebuggerStartInfo : SoftDebuggerStartInfo
    {
        public UnityDebuggerStartInfo(UnityProcess process)
            : base(UnityDebuggerStartInfo.CreateStartArgs(process)) { }

        private static SoftDebuggerStartArgs CreateStartArgs(UnityProcess process)
        {
            IPEndPoint endPoint;
            if (!process.TryGetDebuggerEndPoint(out endPoint))
                throw new InvalidOperationException();
            return (SoftDebuggerStartArgs)new SoftDebuggerConnectArgs("Unity", endPoint.Address, endPoint.Port);
        }
    }
}
