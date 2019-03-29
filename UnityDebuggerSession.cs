using System;
using Mono.Debugging.Soft;

namespace MonoDevelop.Debugger.Soft.Unity
{
    public class UnityDebuggerSession : SoftDebuggerSession
    {
        protected override void OnExit()
        {
            this.Detach();
        }
    }
}
