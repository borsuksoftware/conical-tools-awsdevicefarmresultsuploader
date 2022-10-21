Source code for BorsukSoftware.Conical.Tools.AWSDeviceFarmResultsUploader

For more details on Conical, please see https://conical.cloud

## Purpose
The tool exists to make it easy to upload results from AWS Device farm jobs to Conical.

The tool assumes that the user is using Appium Python tests which have been run with the -rA flag. To extend the tool to support other forms of tests, it would be necessary to add a command line flag to specify how to process the log output in order to extract the details of the test results.

#### 'Stopped jobs'
Because the device farm treats each device as an independent run, it's possible for a given device to fail. This can mean that the necessary output files aren't generated for processing. At this point, the app will create a temporary, failing, test so that this failure information isn't lost.

#### Evidence set creation
The tool will create a test run set per device. These test run sets will then be collated together to form an evidence set representing the whole job. The tool supports specifying the prefix to use per test run set which can have tokens which are replaced at run time. Any property on the job object can be used (reflection is used to extract the appropriate value), e.g.

	{job.device.name}
	{job.device.os}
	{job.device.platform}

Note that these are case-insensitive.

#### Screenshot support
AWS device farm supports the taking of screen shots. The app uses the convention of '{className}_{testName}_{screenShotImageName}.png' to work out which images should be uploaded to Conical for the given test

## Usage
The expectation is that this tool is used as part of CI pipelines 

## Future extensions
Support uploading multiple AWS device farm jobs in a single ES. This would be useful for processing combinations of Apple / Android devices etc.

## Examples
Once the tool has been installed, it can be run with:

```
dotnet tool run BorsukSoftware.Conical.Tools.AWSDeviceFarmResultsUploader

  -server "https://demo.conical.cloud" `
  -token "itsNotOurTokenDontEvenBother:)" `
  -product "AWS-Testing" `
  -testRunType "Appium" `
  -testRunSetName "AWS Device Farm Tests - #4974 - {job.Device.Name}" `
  -testRunSetTag "device-{job.device.name}" `
  -testRunSetTag "{job.device.os}" `
  -testRunSetTag "{job.device.platform}" `
  -testRunSetTag "build-4974" `
  -awsProject "aws-project-name" `
  -awsTestRun "Android - 4974 - 2022-10-19 17:01" `
  -evidenceSetTestRunSetPrefix "{job.device.platform}\{job.device.manufacturer}\{job.device.model} ({job.device.os})" `
  -evidenceSetName "AWS Device Farm Testing (build #)" `
  -evidenceSetDescription "From build #..." `
  -evidenceSetTag "build-4974"

```