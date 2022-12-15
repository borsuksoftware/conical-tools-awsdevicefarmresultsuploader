using Microsoft.VisualStudio.TestPlatform.TestHost;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BorsukSoftware.Conical.Tools.AWSDeviceFarmResultsUploader
{
    public class TextReplacementTests
    {
        [InlineData("{{job.name}", @"{job.name}")]
        [InlineData("bubble\\{job.name}-{job.device.name}", @"bubble\babble-here'sMyName")]
        [Theory]
        public void Standard(string prefix, string expected)
        {
            var job = new Amazon.DeviceFarm.Model.Job()
            {
                Device = new Amazon.DeviceFarm.Model.Device
                {
                    Name = "here'sMyName"
                },
                Name = "babble"
            };

            var result = Program.ProcessString(prefix, job);

            Assert.Equal(expected, result);
        }
    }
}
