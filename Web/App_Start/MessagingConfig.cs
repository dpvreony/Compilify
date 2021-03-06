﻿using System;
using System.Configuration;
using System.Text;
using Compilify.Extensions;
using Compilify.Messaging;
using Compilify.Web.EndPoints;
using MassTransit;
using SignalR;

namespace Compilify.Web
{
    public static class MessagingConfig
    {
        public static void ConfigureServiceBus()
        {
            var connectionString = ConfigurationManager.AppSettings["CLOUDAMQP_URL"].Replace("amqp://", "rabbitmq://");
            var queueName = ConfigurationManager.AppSettings["Compilify.WebMessagingQueue"];

            var endpointAddress = string.Format("{0}/{1}", connectionString, queueName);

            Bus.Initialize(sbc =>
            {
                sbc.UseRabbitMq();
                sbc.ReceiveFrom(endpointAddress);
            });

            Bus.Instance.SubscribeHandler<WorkerResult>(x =>
            {
                var endpoint = GlobalHost.ConnectionManager.GetConnectionContext<ExecuteEndPoint>();
                endpoint.Connection.Send(x.ClientId, new { status = "ok", data = JobRunToString(x) });
            });
        }

        private static string JobRunToString(ICodeRunResult run)
        {
            var builder = new StringBuilder();

            if (!string.IsNullOrEmpty(run.ConsoleOutput))
            {
                builder.AppendLine(run.ConsoleOutput);
            }

            if (!string.IsNullOrEmpty(run.Result))
            {
                builder.AppendLine(run.Result);
            }

            builder.AppendFormat("CPU Time: {0}" + Environment.NewLine, run.ProcessorTime);
            builder.AppendFormat("Bytes Allocated: {0}" + Environment.NewLine, run.TotalMemoryAllocated.ToByteSizeString());

            return builder.ToString();
        }
    }
}