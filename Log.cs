using System;
using System.Collections.Generic;

namespace MonoDevelop.Debugger.Soft.Unity
{
	public static class Log
	{
		public interface ILogger
		{
			void Info (string message);
			void Warning (string message, Exception e);
			void Error (string message, Exception e);
		}

		static List<ILogger> loggers = new List<ILogger>();

		public static void AddLogger(ILogger logger)
		{
			loggers.Add (logger);
		}

		public static void Info(string message)
		{
			foreach (var logger in loggers)
				logger.Info (message);
		}

		public static void Warning(string message, Exception e)
		{
			foreach (var logger in loggers)
				logger.Warning (message, e);
		}

		public static void Error(string message, Exception e)
		{
			foreach (var logger in loggers)
				logger.Error (message, e);
		}
	}
}

