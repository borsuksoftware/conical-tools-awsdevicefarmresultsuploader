using Amazon.DeviceFarm;
using Amazon.DeviceFarm.Model;
using Amazon.Runtime;
using Amazon.Runtime.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BorsukSoftware.Conical.Client.REST;

namespace BorsukSoftware.Conical.Tools.AWSDeviceFarmResultsUploader
{
    public class Program
    {
        private const string CONST_HELPTEXT = @"AWS device farm results uploader
================================

Summary:
This app is designed to make it easy to publish results from an AWS Device Farm test job 
to Conical from the command line. 

This code assumes that the test was run using Appium Python with a -rA flag specified
or at least that the log output file is compatible with pytest.

Security Model:
The security variables are read in from environment variables and as such, they should be set accordingly.

Conical parameters:

 -server XXX                    The conical server
 -product XXX                   The name of the product on the Conical instance
 -token XXX                     The token to use when accessing Conical
 -testRunType XXX               The test run type to upload test runs as
 -testRunSetName XXX            The name to use when uploading test run sets
 -testRunSetDescription XXX     The description to use when uploading test run sets
 -testRunSetTag XXX             Optional tag values
 -testRunSetRefDate XXX         Optional ref date for the created test run sets
 -testRunSetRefDateFormat XXX   Optional date format to use for processing testRunSetRefDate if specified

 -evidenceSetName XXX           The name to use for the created evidence set
 -evidenceSetDescription XXX    The description to use for the created evidence set
 -evidenceSetTag XXX            Optional tag values for the created evidence set
 -evidenceSetRefDate XXX        Optional ref date for the evidence set
 -evidenceSetRefDateFormat XXX  Optional date format to use for processing evidenceSetRefDate if specified

 -evidenceSetTestRunSetPrefix XXX   The prefix to apply to TSRs in the evdence set

AWS Required parameters:
 -awsProject XXX            The name or arn of the AWS device farm project
 -awsTestRun XXX            The name or arn of the test run to upload

Certain parameters are treated as having potential for tokens. These tokens are specified with {..}.
The following tokens are available (case insensitive):

 job.device.manufacturer    The manufacturer of the device which ran the job
 job.device.model
 job.device.formfactor
 job.device.name            The name of the device which ran the job
 job.device.os              The OS version
 job.device.platform        The platform (e.g. Android / iOS)
 job.name                   
 job.result

General:
 -useNonZeroExitCodeOnTestFailure   If specified, and any test within the ES is marked as failure or exception, then a non-zero exit code will be used
 

Others:
 --help                     Show this help text";
        public static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine(CONST_HELPTEXT);
                return 0;
            }

            string awsTestRunName = null, awsParamProject = null;
            string conicalServer = null, conicalProduct = null, conicalToken = null, conicalTestRunType = null;
            string testRunSetName = null, testRunSetDescription = null, testRunSetRefDateStr = null, testRunSetRefDateFormatStr = null;
            string evidenceSetName = null, evidenceSetDescription = null, evidenceSetRefDateStr = null, evidenceSetRefDateFormatStr = null, evidenceSetTestRunSetPrefix = null;
            var testRunSetTags = new List<string>();
            var evidenceSetTags = new List<string>();
            bool useNonZeroExitCodeOnTestFailure = false;
            for (int i = 0; i < args.Length; ++i)
            {
                switch (args[i].ToLower())
                {
                    /* Conical Parameters */
                    case "-server":
                        conicalServer = args[++i];
                        break;

                    case "-product":
                        conicalProduct = args[++i];
                        break;

                    case "-token":
                        conicalToken = args[++i];
                        break;

                    case "-testruntype":
                        conicalTestRunType = args[++i];
                        break;

                    case "-testrunsetname":
                        testRunSetName = args[++i];
                        break;

                    case "-testrunsetdescription":
                        testRunSetDescription = args[++i];
                        break;

                    case "-testrunsetrefdate":
                        testRunSetRefDateStr = args[++i];
                        break;

                    case "-testrunsetrefdateformat":
                        testRunSetRefDateFormatStr = args[++i];
                        break;

                    case "-testrunsettag":
                        testRunSetTags.Add(args[++i]);
                        break;

                    case "-evidencesetname":
                        evidenceSetName = args[++i];
                        break;

                    case "-evidencesetdescription":
                        evidenceSetDescription = args[++i];
                        break;

                    case "-evidencesetrefdate":
                        evidenceSetRefDateStr = args[++i];
                        break;

                    case "-evidencesetrefdateformat":
                        evidenceSetRefDateFormatStr = args[++i];
                        break;

                    case "-evidencesettestrunsetprefix":
                        evidenceSetTestRunSetPrefix = args[++i];
                        break;

                    case "-evidencesettag":
                        evidenceSetTags.Add(args[++i]);
                        break;

                    /* AWS Parameters */
                    case "-awsproject":
                        awsParamProject = args[++i];
                        break;

                    case "-awstestrun":
                        awsTestRunName = args[++i];
                        break;

                    case "-usenonzeroexitcodeontestfailure":
                        useNonZeroExitCodeOnTestFailure = true;
                        break;

                    /* Infrastructural parameters */
                    case "--help":
                        Console.WriteLine(CONST_HELPTEXT);
                        return 0;

                    default:
                        {
                            Console.WriteLine($"Unknown command line arg - {args[i]}");
                            return 1;
                        }
                }
            }

            /** Check inputs (Conical) **/
            if (string.IsNullOrEmpty(conicalServer))
            {
                Console.WriteLine("No Conical server specified");
                return 1;
            }

            if (string.IsNullOrEmpty(conicalProduct))
            {
                Console.WriteLine("No Conical product specified");
                return 1;
            }

            // We don't check the token as anonymous access is permissible (albeit not recommended)

            if (string.IsNullOrEmpty(conicalTestRunType))
            {
                Console.WriteLine("No Conical test run type specified");
                return 1;
            }

            if (string.IsNullOrEmpty(testRunSetName))
            {
                Console.WriteLine("A valid test run set name must be specified");
                return 1;
            }

            /** Handle ES values  **/
            if (string.IsNullOrEmpty(evidenceSetName))
            {
                Console.WriteLine("A valid evidence set name must be specified");
                return 1;
            }

            DateTime? esRefDate = null;
            if (!string.IsNullOrEmpty(evidenceSetRefDateStr))
            {
                if (string.IsNullOrEmpty(evidenceSetRefDateFormatStr))
                {
                    if (!DateTime.TryParse(evidenceSetRefDateStr, out var date))
                    {
                        Console.WriteLine($"Unable to parse '{evidenceSetRefDateStr}' as a valid date");
                        return 1;
                    }

                    esRefDate = date;
                }
                else
                {
                    if (!DateTime.TryParseExact(evidenceSetRefDateStr, evidenceSetRefDateFormatStr, null, System.Globalization.DateTimeStyles.None, out var date))
                    {
                        Console.WriteLine($"Unable to parse '{evidenceSetRefDateStr}' as a valid date using format '{evidenceSetRefDateFormatStr}'");
                        return 1;
                    }

                    esRefDate = date;
                }
            }

            DateTime? trsRefDate = null;
            if (!string.IsNullOrEmpty(testRunSetRefDateStr))
            {
                if (string.IsNullOrEmpty(testRunSetRefDateFormatStr))
                {
                    if (!DateTime.TryParse(testRunSetRefDateStr, out var date))
                    {
                        Console.WriteLine($"Unable to parse '{testRunSetRefDateStr}' as a valid date");
                        return 1;
                    }

                    trsRefDate = date;
                }
                else
                {
                    if (!DateTime.TryParseExact(testRunSetRefDateStr, testRunSetRefDateFormatStr, null, System.Globalization.DateTimeStyles.None, out var date))
                    {
                        Console.WriteLine($"Unable to parse '{testRunSetRefDateStr}' as a valid date using format '{testRunSetRefDateFormatStr}'");
                        return 1;
                    }

                    trsRefDate = date;
                }
            }

            /** Check inputs (AWS) **/
            if (string.IsNullOrEmpty(awsParamProject))
            {
                Console.WriteLine("No AWS project specified");
                return 1;
            }

            if (string.IsNullOrEmpty(awsTestRunName))
            {
                Console.WriteLine("No test run specified");
                return 1;
            }

            Console.WriteLine("Creating farm client - USWest2");
            var amazonDeviceFarmClient = new AmazonDeviceFarmClient(Amazon.RegionEndpoint.USWest2);
            var amazonS3Client = new Amazon.S3.AmazonS3Client(Amazon.RegionEndpoint.USWest2);


            /************************************ Fetch all details from AWS ***********************************/
            var projectArn = awsParamProject;
            if (!awsParamProject.StartsWith("arn:"))
            {
                Console.WriteLine("Sourcing project details from AWS");
                var projectList = await amazonDeviceFarmClient.ListProjectsAsync(new ListProjectsRequest());
                var project = projectList.Projects.SingleOrDefault(p => StringComparer.InvariantCultureIgnoreCase.Compare(awsParamProject, p.Name) == 0);
                if (project == null)
                {
                    Console.WriteLine($"No project name '{awsParamProject}' found");
                    return 1;
                }

                projectArn = project.Arn;
            }

            var testRunArn = awsTestRunName;
            if (!testRunArn.StartsWith("arn:"))
            {
                Console.WriteLine("Sourcing test run details from AWS");
                var testRuns = await amazonDeviceFarmClient.ListRunsAsync(new ListRunsRequest { Arn = projectArn });
                var testRun = testRuns.Runs.SingleOrDefault(tr => StringComparer.InvariantCultureIgnoreCase.Compare(testRunArn, tr.Name) == 0);
                if (testRun == null)
                {
                    Console.WriteLine($"No test run named '{awsTestRunName}' found");
                    return 1;
                }

                testRunArn = testRun.Arn;
            }

            Console.WriteLine("AWS Settings:");
            Console.WriteLine($" Project: {projectArn}");
            Console.WriteLine($" Test Run: {testRunArn}");
            Console.WriteLine();

            Console.WriteLine("Creating farm client");

            var uniqueID = Guid.NewGuid();

            Console.WriteLine("Sourcing run");
            var run = await amazonDeviceFarmClient.GetRunAsync(testRunArn);
            if (run.Run.Status != ExecutionStatus.COMPLETED)
            {
                Console.WriteLine($" => Not complete - {run.Run.Status}");
                Console.WriteLine(" => Aborting");
                return 1;
            }

            string testRunFullUrl = GetAWSConsoleLinkForTestRun(testRunArn);

            var jobs = await amazonDeviceFarmClient.ListJobsAsync(new Amazon.DeviceFarm.Model.ListJobsRequest { Arn = testRunArn });

            var client = new BorsukSoftware.Conical.Client.REST.AccessLayer(conicalServer, conicalToken);
            Client.IProduct product;
            try
            {
                product = await client.GetProduct(conicalProduct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to source product from the Conical server - {ex}");
                return 1;
            }

            var sources = new List<(string Prefix, string Product, int TestRunSetID, Client.EvidenceSetTestRunSelectionMode TestRunSelectionMode, IReadOnlyCollection<int> testRunIDs)>();
            foreach (var job in jobs.Jobs)
            {
                Console.WriteLine($" Job - {job.Name} ({@job.Device.Name}): {job.Result}");
                Console.WriteLine($"  Device: {job.Device.Platform} / {job.Device.Os} / {job.Device.Model} / {job.Device.FormFactor}");

                var trsName = testRunSetName;
                var trsDescription = testRunSetDescription;
                if (!string.IsNullOrEmpty(trsName))
                    trsName = ProcessString(trsName, job);
                if (!string.IsNullOrEmpty(trsDescription))
                    trsDescription = ProcessString(trsDescription, job);

                var tagsArray = testRunSetTags.Select(t => ProcessString(t, job)).ToArray();
                var trs = await product.CreateTestRunSet(trsName,
                    trsDescription,
                    trsRefDate,
                    tagsArray);

                await trs.PublishExternalLinks(new[] { new Client.ExternalLink("AWS Job", testRunFullUrl, "AWS Job for the whole set") });

                string prefix = ProcessString(evidenceSetTestRunSetPrefix, job);
                sources.Add((prefix, product.Name, trs.ID, BorsukSoftware.Conical.Client.EvidenceSetTestRunSelectionMode.All, null));

                var suites = await amazonDeviceFarmClient.ListSuitesAsync(new Amazon.DeviceFarm.Model.ListSuitesRequest
                {
                    Arn = job.Arn
                });

                var testsSuite = suites.Suites.SingleOrDefault(s => s.Name == "Tests Suite");
                if (testsSuite == null)
                {
                    Console.WriteLine("No 'Tests Suite' suite found");
                    continue;
                }

                Console.WriteLine($"  Suite - {testsSuite.Name}: {testsSuite.Result}");

                var allFileArtefacts = await amazonDeviceFarmClient.ListArtifactsAsync(new ListArtifactsRequest { Arn = testsSuite.Arn, Type = ArtifactCategory.FILE });

                bool potentiallyIncompleteResults =
                    testsSuite.Result == ExecutionResult.ERRORED ||
                    testsSuite.Result == ExecutionResult.STOPPED;
                if (potentiallyIncompleteResults)
                {
                    Console.WriteLine("   => Potentially incomplete results, adding additional test run 'General'");

                    var tr = await trs.CreateTestRun("General", "Automatically added TR", conicalTestRunType, Client.TestRunStatus.Failed);

                    var testSpecOutputArtefact = allFileArtefacts.Artifacts.SingleOrDefault(a => StringComparer.InvariantCultureIgnoreCase.Compare("Test spec output", a.Name) == 0);
                    if (testSpecOutputArtefact != null)
                    {
                        var request = new System.Net.Http.HttpRequestMessage(HttpMethod.Get, testSpecOutputArtefact.Url);
                        var httpClient = new System.Net.Http.HttpClient();
                        using (var response = await httpClient.SendAsync(request))
                        {
                            response.EnsureSuccessStatusCode();

                            Console.WriteLine("   => Processing");
                            using (var sourceStream = await response.Content.ReadAsStreamAsync())
                            {
                                var lines = new List<string>();
                                using (var streamReader = new System.IO.StreamReader(sourceStream))
                                {
                                    while (true)
                                    {
                                        var line = streamReader.ReadLine();
                                        if (line == null)
                                            break;

                                        lines.Add(line);
                                    }
                                    await tr.PublishTestRunLogMessages(lines);
                                }
                            }
                        }
                    }
                }

                {
                    var testSpecOutputArtefact = allFileArtefacts.Artifacts.SingleOrDefault(a => StringComparer.InvariantCultureIgnoreCase.Compare("Test spec output", a.Name) == 0);
                    if (testSpecOutputArtefact == null)
                    {
                        Console.WriteLine("   => Cannot find 'Test spec output', skipping");
                        continue;
                    }

                    Console.WriteLine("   => Found 'Test spec output'");

                    var screenShotArtefacts = await amazonDeviceFarmClient.ListArtifactsAsync(new ListArtifactsRequest { Arn = testsSuite.Arn, Type = ArtifactCategory.SCREENSHOT });
                    var screenShotArtefactsByName = screenShotArtefacts.Artifacts.ToDictionary(a => a.Name);


                    var request = new System.Net.Http.HttpRequestMessage(HttpMethod.Get, testSpecOutputArtefact.Url);
                    var httpClient = new System.Net.Http.HttpClient();
                    using (var response = await httpClient.SendAsync(request))
                    {
                        response.EnsureSuccessStatusCode();

                        // Take a local copy of the result so that we can process it multiple times
                        System.IO.Stream cachedStream;
                        using (var sourceStream = await response.Content.ReadAsStreamAsync())
                        {
                            var tempFile = System.IO.Path.GetTempFileName();
                            cachedStream = new System.IO.FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite);
                            sourceStream.CopyTo(cachedStream);
                            cachedStream.Flush();
                        }

                        // Upload the whole test spec file
                        Console.WriteLine("    => Uploading test spec as additional file");
                        cachedStream.Position = 0;
                        await trs.PublishAdditionalFile("Test spec output.txt", "Full test spec output from AWS", cachedStream);

                        Console.WriteLine("    => Processing");
                        // Parse the test results
                        cachedStream.Position = 0;
                        var logProcessor = new BorsukSoftware.Utils.Pytest.OutputProcessor.Processor();
                        var streamReader = new System.IO.StreamReader(cachedStream);
                        var output = logProcessor.ProcessLogOutput(streamReader);

                        Console.WriteLine($"     => uploading {output.Count} test(s) to Conical");
                        foreach (var entry in output)
                        {
                            Console.WriteLine($"     => {entry.TestName}");
                            var adjustedName = string.Join("\\", entry.TestName.Split('.'));

                            var tr = await trs.CreateTestRun(adjustedName,
                                "Device farm test",
                                conicalTestRunType,
                                entry.Passed ? Client.TestRunStatus.Passed : Client.TestRunStatus.Failed);

                            var screenShotPrefix = string.Join("_", entry.TestName.Split('.').Append(String.Empty));
                            foreach (var screenShotPair in screenShotArtefactsByName.Where(key => key.Key.StartsWith(screenShotPrefix)))
                            {
                                Console.WriteLine($"      => Uploading screenshot '{screenShotPair.Key}'");

                                var screenShotRequest = new System.Net.Http.HttpRequestMessage(HttpMethod.Get, screenShotPair.Value.Url);
                                using (var screenShotResponse = await httpClient.SendAsync(screenShotRequest))
                                {
                                    var screenShotName = $"{screenShotPair.Value.Name.Substring(screenShotPrefix.Length)}.{screenShotPair.Value.Extension}";
                                    screenShotResponse.EnsureSuccessStatusCode();

                                    using (var screenShotSourceStream = await screenShotResponse.Content.ReadAsStreamAsync())
                                    {
                                        await tr.PublishTestRunAdditionalFile(screenShotName, "From AWS", screenShotSourceStream);
                                    }
                                }
                            }

                            var fullLogs = Enumerable.Empty<string>();

                            if (entry.Body.Count > 0)
                            {
                                fullLogs = fullLogs.Append("=== BODY ===");
                                fullLogs = fullLogs.Concat(entry.Body);
                                fullLogs = fullLogs.Append(string.Empty);
                            }

                            if (entry.StdOutLines.Count > 0)
                            {
                                fullLogs = fullLogs.Append("=== STD OUT ===");
                                fullLogs = fullLogs.Concat(entry.StdOutLines);
                                fullLogs = fullLogs.Append(string.Empty);
                            }

                            if (entry.LogMessages.Count > 0)
                            {
                                fullLogs = fullLogs.Append("=== LOGS ===");
                                fullLogs = fullLogs.Concat(entry.LogMessages);
                                fullLogs = fullLogs.Append(string.Empty);
                            }

                            await tr.PublishTestRunLogMessages(fullLogs);
                        }
                    }
                }

                await trs.SetStatus(Client.TestRunSetStatus.Standard);
            }

            Console.WriteLine("Creating evidence set");
            var es = await product.CreateEvidenceSet(evidenceSetName,
                evidenceSetDescription,
                esRefDate,
                evidenceSetTags,
                new[] { (name: "AWS Job", Url: testRunFullUrl, Description: "AWS Job for the whole set") },
                Client.EvidenceSetTestMultipleSourceTestRunsBehaviour.NotAllowed,
                sources);

            Console.WriteLine($" => #{es.ID}");

            if (useNonZeroExitCodeOnTestFailure)
            {
                Console.WriteLine();
                Console.WriteLine("Operating in useNonZeroExitCodeOnTestFailure");
                Console.WriteLine(" => checking evidence set");

                Console.WriteLine($" => Successful tests: {es.SuccessfulTests}");
                Console.WriteLine($" => Failed tests: {es.FailedTests}");
                Console.WriteLine($" => Erroring tests: {es.ErroringTests}");

                if (es.FailedTests > 0 || es.ErroringTests > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine(" => failing with exit code of 2");
                    return 2;
                }
            }

            return 0;
        }

        #region Utils

        /// <summary>
        /// Create the URL for browsing the job on AWS's management console
        /// </summary>
        private static string GetAWSConsoleLinkForTestRun(string testRunArn)
        {
            var guidsStr = testRunArn.Split(':').Last();
            var guidSet = guidsStr.Split('/').Select(n => Guid.Parse(n)).ToList();

            var output = $"https://us-west-2.console.aws.amazon.com/devicefarm/home#/mobile/projects/{guidSet[0]}/runs/{guidSet[1]}";

            return output;
        }

        #endregion

        #region Text Replacement

        public static string ProcessString(string start, Amazon.DeviceFarm.Model.Job job)
        {
            var output = new StringBuilder();

            bool wasBracket = false;
            var insideBracketCharacters = new StringBuilder();
            foreach (var c in start)
            {
                if (wasBracket)
                {
                    if (c == '{')
                    {
                        if (insideBracketCharacters.Length == 0)
                        {
                            wasBracket = false;
                            output.Append('{');
                            continue;
                        }
                        else
                        {
                            throw new System.InvalidOperationException("'{' found in middle of existing bracket");
                        }
                    }
                    if (c == '}')
                    {
                        // Time to do the magic...
                        var str = insideBracketCharacters.ToString();

                        var splitStr = str.Split('.');
                        if (StringComparer.InvariantCultureIgnoreCase.Compare("job", splitStr[0]) != 0)
                            throw new System.InvalidOperationException($"Don't know how to handle root property '{splitStr[0]}'");

                        object obj = job;
                        for (int i = 1; i < splitStr.Length; i++)
                        {
                            if (obj == null)
                                continue;

                            var property = obj.GetType().GetProperties().SingleOrDefault(pi => StringComparer.InvariantCultureIgnoreCase.Compare(splitStr[i], pi.Name) == 0);
                            if (property == null)
                                throw new System.InvalidOperationException($"Unknown property '{splitStr[i]}'");

                            obj = property.GetValue(obj);
                        }

                        output.Append(obj);
                        wasBracket = false;
                    }
                    else
                    {
                        insideBracketCharacters.Append(c);
                    }
                }
                else
                {
                    if (c == '{')
                    {
                        wasBracket = true;
                        insideBracketCharacters = new StringBuilder(); ;
                    }
                    else
                        output.Append(c);
                }
            }

            if (wasBracket)
                throw new System.InvalidOperationException("Unclosed { found");

            var outputStr = output.ToString();
            return outputStr;
        }

        #endregion
    }
}