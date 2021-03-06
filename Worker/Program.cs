﻿using System;
using System.Configuration;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Compilify.LanguageServices;
using Compilify.Messaging;
using Compilify.Models;
using MassTransit;
using NLog;

namespace Compilify.Worker
{
    public sealed class Program
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly ICodeCompiler Compiler = new CSharpCompiler();

        private static readonly ManualResetEvent ResetEvent = new ManualResetEvent(false);

        public static int Main(string[] args)
        {
            Logger.Info("Application started.");

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledApplicationException;

            // Log the exception, but do not mark it as observed. The process will be terminated and restarted 
            // automatically by AppHarbor
            TaskScheduler.UnobservedTaskException +=
                (sender, e) =>
                {
                    Bus.Shutdown();
                    ResetEvent.Set();

                    Logger.ErrorException("An unobserved task exception occurred", e.Exception);
                };

            var connectionString = ConfigurationManager.AppSettings["CLOUDAMQP_URL"].Replace("amqp://", "rabbitmq://");
            var queueName = ConfigurationManager.AppSettings["Compilify.WorkerMessagingQueue"];

            var endpointAddress = string.Format("{0}/{1}", connectionString, queueName);

            Bus.Initialize(
                sbc =>
                {
                    sbc.UseRabbitMq();
                    sbc.ReceiveFrom(endpointAddress);
                    sbc.Subscribe(subs => subs.Handler<EvaluateCodeCommand>(ProcessCommand));
                });

            ResetEvent.WaitOne();

            Bus.Shutdown();

            return -1;
        }

        private static void ProcessCommand(EvaluateCodeCommand cmd)
        {
            if (cmd == null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var timeInQueue = now - cmd.Submitted;

            Logger.Info("Job received after {0:N3} seconds in queue.", timeInQueue.TotalSeconds);

            if (now > cmd.Expires)
            {
                Logger.Warn("Job was in queue for longer than {0} seconds, skipping!", (cmd.Expires - cmd.Submitted).Seconds);
                return;
            }

            var startedOn = DateTimeOffset.UtcNow;
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            var assembly = Compiler.Compile(cmd);

            ExecutionResult result;
            if (assembly == null)
            {
                result = new ExecutionResult
                {
                    Result = "[compiling of code failed]"
                };
            }
            else
            {
                using (var executor = new Sandbox())
                {
                    result = executor.Execute(assembly, TimeSpan.FromSeconds(5));
                }
            }

            stopWatch.Stop();
            var stoppedOn = DateTime.UtcNow;

            Logger.Info("Work completed in {0} milliseconds.", stopWatch.ElapsedMilliseconds);

            var response = new WorkerResult
            {
                ClientId = cmd.ClientId,
                StartTime = startedOn,
                StopTime = stoppedOn,
                RunDuration = stopWatch.Elapsed,
                ProcessorTime = result.ProcessorTime,
                TotalMemoryAllocated = result.TotalMemoryAllocated,
                ConsoleOutput = result.ConsoleOutput,
                Result = result.Result
            };

            Bus.Instance.Publish(response);

            stopWatch.Reset();
        }

        public static void OnUnhandledApplicationException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;

            if (e.IsTerminating)
            {
                Logger.FatalException("An unhandled exception is causing the worker to terminate.", exception);
            }
            else
            {
                Logger.ErrorException("An unhandled exception occurred in the worker process.", exception);
            }

            Bus.Shutdown();
            ResetEvent.Set();
        }
    }
}
