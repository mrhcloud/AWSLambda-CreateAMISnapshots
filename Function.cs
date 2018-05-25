using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.Lambda.Core;
using System;
using System.Collections.Generic;
using System.Net;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]
namespace Amazon.Lambda.Function
{
    public class LambdaFunction
    {
        Boolean EnableCloudWatchLogs = false;
        Int16 DefaultRetentionPeriod = 30;

        public async void CreateAMISnapshotsHandler(ILambdaContext Context)
        {
            // Check for Environment variable to enable cloud watch logging
            if(System.Environment.GetEnvironmentVariable("LogEnabled") != null)
            {
                Boolean.TryParse(System.Environment.GetEnvironmentVariable("LogEnabled"), out EnableCloudWatchLogs);
            }

            // Get default retention period if not specified on an EC2 Instance
            if(System.Environment.GetEnvironmentVariable("DefaultRetention") != null)
            {
                Int16.TryParse(System.Environment.GetEnvironmentVariable("DefaultRetention"), out DefaultRetentionPeriod);
            }

            // EC2 Client
            IAmazonEC2 EC2Client = new AmazonEC2Client();

            // Get All EC2 instances
            DescribeInstancesResponse EC2Instances = await EC2Client.DescribeInstancesAsync(new DescribeInstancesRequest());

            // New list of instances to backup
            IList<Instance> BackupInstances = new List<Instance>();

            // Count of number of Instances
            Int16 Instances = 0;

            // Count of number of Instances to backup
            Int16 InstancesBackup = 0;

            // Check each instance for Backup Requirement
            foreach(Reservation Reservation in EC2Instances.Reservations)
            {
                foreach(Instance Instance in Reservation.Instances)
                {
                    Instances++;

                    // Check for Backup requirement
                    if(Instance.Tags.Find(t => t.Key.Equals("Backup")) != null &&
                       Instance.Tags.Find(t => t.Key.Equals("Backup")).Value.ToLowerInvariant().Equals("true"))
                    {
                        InstancesBackup++;
                        BackupInstances.Add(Instance);
                    }
                }
            }

            // Log number of instances and number to backup
            Log(String.Format("{0} EC2 Instances, {1} EC2 Instances to Snapshot", Instances, InstancesBackup));

            // Loop through all instances to backup
            foreach(Instance Instance in BackupInstances)
            {
                Int16 RetentionPeriod = 0;
                String EC2InstanceName = "No Name";

                // Get Instance Name tag
                if(Instance.Tags.Find(t => t.Key.Equals("Name")) != null)
                    EC2InstanceName = Instance.Tags.Find(t => t.Key.Equals("Name")).Value;

                // Get Retention Period for EC2 Instance
                if(Instance.Tags.Find(t => t.Key.Equals("RetentionPeriod")) != null)
                    Int16.TryParse(Instance.Tags.Find(t => t.Key.Equals("RetentionPeriod")).Value, out RetentionPeriod);

                // Check for Retention Period, use default if null or 0
                if(RetentionPeriod == 0) RetentionPeriod = DefaultRetentionPeriod;

                Log(String.Format("Requesting AMI Snapshot of {0} ({1}) on {2}", EC2InstanceName, Instance.InstanceId, DateTime.UtcNow.ToString("dd-MM-yyyy")));

                // Request creation of EC2 AMI Snapshot
                try
                {
                    CreateImageResponse AMIImage = await EC2Client.CreateImageAsync(new CreateImageRequest(){
                        Description = String.Format(Constants.AMISnapshotDescription, EC2InstanceName, Instance.InstanceId, DateTime.UtcNow.ToString("dd-MM-yyyy")),
                        InstanceId = Instance.InstanceId,
                        Name = String.Format(Constants.AMISnapshotDescription, EC2InstanceName, Instance.InstanceId, DateTime.UtcNow.ToString("dd-MM-yyyy")),
                        NoReboot = true
                    });

                    // Tag new AMI snapshot
                    CreateTagsResponse TagsResponse = await EC2Client.CreateTagsAsync(new CreateTagsRequest()
                    {
                        Resources = new List<String>()
                        {
                            AMIImage.ImageId
                        },
                        Tags = (List<Tag>)GenerateAMITags(EC2InstanceName, Instance.InstanceId, DateTime.UtcNow.ToString("dd-MM-yyyy"), RetentionPeriod)
                    });

                    if(TagsResponse.HttpStatusCode.Equals(HttpStatusCode.OK))
                        Log(String.Format("AMI Snapshot and tag request successful for {0}", AMIImage.ImageId));
                }
                catch(Exception Exception)
                {
                    Log(Exception.StackTrace);
                }
            }
        }

        void Log(String Message)
        {
            if(EnableCloudWatchLogs)
            {
                Console.WriteLine(Message);
            }
        }

        IList<Tag> GenerateAMITags(String InstanceName, String InstanceId, String Date, Int16 RetentionPeriod)
        {
            return new List<Tag>()
            {
                new Tag()
                {
                    Key = "Name",
                    Value = String.Format(Constants.AMISnapshotDescription, InstanceName, InstanceId, Date)
                },
                new Tag()
                {
                    Key = "DeleteOn",
                    Value = DateTime.UtcNow.AddDays(RetentionPeriod).ToString("dd-MM-yyyy")
                }
            };
        }
    }
}