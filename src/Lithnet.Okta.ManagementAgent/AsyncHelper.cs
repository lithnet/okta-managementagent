using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    internal static class AsyncHelper
    {
        private static readonly TaskFactory factory = new TaskFactory(CancellationToken.None, TaskCreationOptions.None, TaskContinuationOptions.None, TaskScheduler.Default);
        
        // This function destroys ILMerge, so we switched to ILRepack instead. The GetAwaiter() call seems to be responsible for making ILMerge hang
        public static TResult RunSync<TResult>(Task<TResult> func)
        {
            return factory.StartNew(() => func).Unwrap().GetAwaiter().GetResult();
        }

        public static TResult RunSync<TResult>(Task<TResult> func, CancellationToken token)
        {
            return factory.StartNew(() => func, token).Unwrap().GetAwaiter().GetResult();
        }

        public static void RunSync(Task func)
        {
            factory.StartNew(() => func).Unwrap().GetAwaiter().GetResult();
        }

        public static void RunSync(Task func, CancellationToken token)
        {
            factory.StartNew(() => func, token).Unwrap().GetAwaiter().GetResult();
        }
    }
}
