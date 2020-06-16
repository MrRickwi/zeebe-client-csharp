﻿//
//    Copyright (c) 2018 camunda services GmbH (info@camunda.com)
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NLog.Extensions.Logging;
using Zeebe.Client;
using Zeebe.Client.Api.Responses;
using Zeebe.Client.Api.Worker;
using Zeebe.Client.Impl.Builder;

namespace Client.Examples
{
    internal class Program
    {
        private const string AuthServer = "https://login.cloud.ultrawombat.com/oauth/token";
        private const string ClientId = "KdxEcZ9PfistlkitIyh6Y9eFXTBqjsap";
        private const string ClientSecret = "23upPNVO9GbOBQDuEIWV6sIok6b_Z8JycpAoXFcr6mbfX7Vo21GoLeysPTicaNpE";
        private const string Audience = "ba4207a1-93b5-4526-afb1-1bdd417a566e.zeebe.ultrawombat.com";
        private const string ZeebeUrl = Audience + ":443";


        private static readonly string DemoProcessPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "ten_tasks.bpmn");
        private static readonly string PayloadPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "big_payload.json");
//        private static readonly string ZeebeUrl = "0.0.0.0:26500";
        private static readonly string WorkflowInstanceVariables = "{\"a\":\"123\"}";
        private static readonly string JobType = "benchmark-task";
        private static readonly string WorkerName = Environment.MachineName;
        private static readonly long WorkCount = 1_000_000L;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static async Task Main(string[] args)
        {
            // create zeebe client
            var client = ZeebeClient.Builder()
                .UseLoggerFactory(new NLogLoggerFactory())
                .UseGatewayAddress(ZeebeUrl)
                .UseTransportEncryption()
                .UseAccessTokenSupplier(
                    CamundaCloudTokenProvider.Builder()
                        .UseAuthServer(AuthServer)
                        .UseClientId(ClientId)
                        .UseClientSecret(ClientSecret)
                        .UseAudience(Audience)
                        .Build())
                .Build();

            var topology = await client.TopologyRequest().Send();

            Logger.Info(topology.ToString);
            Console.WriteLine(topology);
            // deploy
            var deployResponse = await client.NewDeployCommand()
                .AddResourceFile(DemoProcessPath)
                .Send();

            // create workflow instance
            var workflowKey = deployResponse.Workflows[0].WorkflowKey;
            var bigPayload = File.ReadAllText(PayloadPath);

            Task.Run(async () =>
            {

                while (true)
                {
                    var start = DateTime.Now;
                    for (var i = 0; i < 100; i++)
                    {
                        try {
                        await client
                            .NewCreateWorkflowInstanceCommand()
                            .WorkflowKey(workflowKey)
                            .Variables(bigPayload)
                            .Send();
                        } catch (Exception e) {

                        }
                    }
                    var endTime = DateTime.Now;
                    var diff = endTime.Millisecond - start.Millisecond;
                    if (diff < 1000)
                    {
                        Thread.Sleep(1000 - diff);
                    }
                }


            });

            // open job worker
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset))
            {
                client.NewWorker()
                      .JobType(JobType)
                      .Handler(HandleJob)
                      .MaxJobsActive(120)
                      .Name(WorkerName)
                      .AutoCompletion()
                      .PollInterval(TimeSpan.FromMilliseconds(100))
                      .Timeout(TimeSpan.FromSeconds(10))
                      .PollingTimeout(TimeSpan.FromSeconds(30))
                      .Open();

                // blocks main thread, so that worker can run
                signal.WaitOne();
            }
        }

        private static void HandleJob(IJobClient jobClient, IJob job)
        {
            Logger.Debug("Handle job {job}", job.Key);
            jobClient.NewCompleteJobCommand(job).Send();
        }
    }
}