using System;
using Mono.Debugging.Client;

namespace MonoDevelop.Debugger.Soft.Unity
{
    public class UnityProcessInfo : ProcessInfo
    {
        public string ProjectName { get; set; }

        public UnityProcessInfo(long id, string name, string projectName)
            : base(id, name)
        {
            ProjectName = projectName;
        }
    }
}
