﻿using Microsoft.Research.MultiWorldTesting.JoinUploader;
using Microsoft.Research.MultiWorldTesting.ExploreLibrary;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Research.MultiWorldTesting.ClientLibrary.MultiAction;
using Microsoft.Research.MultiWorldTesting.ExploreLibrary.MultiAction;
using System.IO;
using VW;

namespace ClientDecisionServiceSample
{
    public class MultiActionSamples
    {
        public static void SampleCodeUsingASAJoinServer()
        {
            // Create configuration for the decision service
            var serviceConfig = new DecisionServiceConfiguration<ExpandedContext, ExpandedActionDependentFeatures>(
                authorizationToken: "sample-code",
                explorer: new EpsilonGreedyExplorer<ExpandedContext>(new ExpandedPolicy(), epsilon: 0.8f))
            {
                PollingForModelPeriod = TimeSpan.MinValue,
                PollingForSettingsPeriod = TimeSpan.MinValue,
                JoinServerType = Microsoft.Research.MultiWorldTesting.ClientLibrary.JoinServerType.AzureStreamAnalytics,
                //EventHubConnectionString = "Endpoint=sb://mwtbus.servicebus.windows.net/;SharedAccessKeyName=MWTASA;SharedAccessKey=Gt6SZtMJvESLQM74pfZyaYwYbn7X5YHBqi1QntpooNc=",
                //EventHubInputName = "Impressions"
                EventHubConnectionString = "Endpoint=sb://mwtbus.servicebus.windows.net/;SharedAccessKeyName=shared-policy-scratch;SharedAccessKey=MqKvUJ/ZqBYC28izl0hgzdSmt9b3JvA2uUdncV4lRJA=",
                EventHubInputName = "eh-scratch"
            };

            var service = new DecisionService<ExpandedContext, ExpandedActionDependentFeatures>(serviceConfig);

            //string uniqueKey = "sample-asa-client-";
            string uniqueKey = "scratch-key-";

            var rg = new Random(uniqueKey.GetHashCode());

            var vwPolicy = new VWPolicy<ExpandedContext, ExpandedActionDependentFeatures>(ExpandedContext.GetFeaturesFromContext);

            for (int i = 1; i < 20; i++)
            {
                int numActions = rg.Next(5, 10);

                DateTime timeStamp = DateTime.UtcNow;
                string key = uniqueKey + Guid.NewGuid().ToString();

                uint[] action = service.ChooseAction(new UniqueEventID { Key = key, Id = 1, TimeStamp = timeStamp }, ExpandedContext.CreateRandom(numActions, rg), (uint)numActions);
                service.ReportReward(i / 100f, new UniqueEventID { Key = key, Id = 0, TimeStamp = timeStamp });

                System.Threading.Thread.Sleep(1);
            }

            service.Flush();
        }
        public static void SampleCodeUsingEventHubUploader()
        {
            var uploader = new EventUploaderASA("", "");

            Stopwatch sw = new Stopwatch();

            int numEvents = 100;
            var events = new IEvent[numEvents];
            for (int i = 0; i < numEvents; i++)
            {
                events[i] = new SingleActionInteraction
                {
                    Key = i.ToString(),
                    Action = 1,
                    Context = "context " + i,
                    Probability = (float)i / numEvents
                };
            }
            //await uploader.UploadAsync(events[0]);
            uploader.Upload(events[0]);

            sw.Start();

            //await uploader.UploadAsync(events.ToList());
            uploader.Upload(events.ToList());

            events = new IEvent[numEvents];
            for (int i = 0; i < numEvents; i++)
            {
                events[i] = new Observation
                {
                    Key = i.ToString(),
                    Value = "observation " + i
                };
            }
            //await uploader.UploadAsync(events.ToList());
            uploader.Upload(events.ToList());

            Console.WriteLine(sw.Elapsed);
        }

        public static void SampleCodeUsingActionDependentFeatures()
        {
            // Create configuration for the decision service
            var serviceConfig = new DecisionServiceConfiguration<ADFContext, ADFFeatures>(
                authorizationToken: "",
                explorer: new EpsilonGreedyExplorer<ADFContext>(new ADFPolicy(), epsilon: 0.8f))
            {
                PollingForModelPeriod = TimeSpan.MinValue,
                PollingForSettingsPeriod = TimeSpan.MinValue
            };

            var service = new DecisionService<ADFContext, ADFFeatures>(serviceConfig);

            string uniqueKey = "eventid";

            var rg = new Random(uniqueKey.GetHashCode());

            var vwPolicy = new VWPolicy<ADFContext, ADFFeatures>(GetFeaturesFromContext);

            for (int i = 1; i < 100; i++)
            {
                if (i == 30)
                {
                    string vwModelFile = TrainNewVWModelWithRandomData(numExamples: 5, numActions: 10);

                    vwPolicy = new VWPolicy<ADFContext, ADFFeatures>(GetFeaturesFromContext, vwModelFile);

                    // Alternatively, VWPolicy can also be loaded from an IO stream:
                    // var vwModelStream = new MemoryStream(File.ReadAllBytes(vwModelFile));
                    // vwPolicy = new VWPolicy<ADFContext, ADFFeatures>(GetFeaturesFromContext, vwModelStream);

                    // Manually updates decision service with a new policy for consuming VW models.
                    service.UpdatePolicy(vwPolicy);
                }
                if (i == 60)
                {
                    string vwModelFile = TrainNewVWModelWithRandomData(numExamples: 6, numActions: 8);

                    // Evolves the existing VWPolicy with a new model
                    vwPolicy.ModelUpdate(vwModelFile);
                }

                int numActions = rg.Next(5, 10);
                uint[] action = service.ChooseAction(new UniqueEventID { Key = uniqueKey }, ADFContext.CreateRandom(numActions, rg), (uint)numActions);
                service.ReportReward(i / 100f, new UniqueEventID { Key = uniqueKey });
            }

            service.Flush();
        }

        /// <summary>
        /// Train a contextual bandit with action dependent features VW model using randomly generated data.
        /// </summary>
        /// <param name="numExamples">Number of examples to generate.</param>
        /// <param name="numActions">Number of actions to use to generate action dependent features for each example.</param>
        /// <returns>New VW model file path.</returns>
        public static string TrainNewVWModelWithRandomData(int numExamples, int numActions)
        {
            Random rg = new Random(numExamples + numActions);

            string vwFileName = string.Format("sample_vw_{0}.model", numExamples);
            if (File.Exists(vwFileName))
            {
                return vwFileName;
            }

            string vwArgs = "--cb_adf --rank_all --quiet";

            using (var vw = new VowpalWabbit<ADFContext, ADFFeatures>(vwArgs))
            {
                //Create examples
                for (int ie = 0; ie < numExamples; ie++)
                {
                    // Create features
                    var context = ADFContext.CreateRandom(numActions, rg);
                    if (ie == 0)
                    {
                        context.Shared = new string[] { "s_1", "s_2" };
                    }

                    vw.Learn(
                        context,
                        context.ActionDependentFeatures,
                        context.ActionDependentFeatures.IndexOf(f => f.Label != null),
                        context.ActionDependentFeatures.First(f => f.Label != null).Label);
                }

                vw.Native.SaveModel(vwFileName);
            }
            return vwFileName;
        }

        private static uint GetNumberOfActionsFromAdfContext(ADFContext context)
        {
            return (uint)context.ActionDependentFeatures.Count;
        }

        private static IReadOnlyCollection<ADFFeatures> GetFeaturesFromContext(ADFContext context)
        {
            return context.ActionDependentFeatures;
        }
    }
}
