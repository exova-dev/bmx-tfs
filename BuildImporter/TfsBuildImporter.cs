﻿using System;
using System.Linq;
using Inedo.BuildMaster;
using Inedo.BuildMaster.Artifacts;
using Inedo.BuildMaster.Data;
using Inedo.BuildMaster.Extensibility;
using Inedo.BuildMaster.Extensibility.Agents;
using Inedo.BuildMaster.Extensibility.BuildImporters;
using Inedo.BuildMaster.Files;
using Inedo.BuildMaster.Web;
using Inedo.Data;

namespace Inedo.BuildMasterExtensions.TFS.BuildImporter
{
    [BuildImporterProperties(
        "TFS",
        "Imports artifacts from TFS.",
        typeof(TfsBuildImporterTemplate))]
    [CustomEditor(typeof(TfsBuildImporterEditor))]
    public sealed class TfsBuildImporter : BuildImporterBase, ICustomBuildNumberProvider
    {
        [Persistent]
        public string ArtifactName { get; set; }
        [Persistent]
        public string BuildDefinition { get; set; }
        [Persistent]
        public string TeamProject { get; set; }
        [Persistent]
        public string TfsBuildNumber { get; set; }
        [Persistent]
        public bool IncludeUnsuccessful { get; set; }

        public string BuildNumber { get; set; }

        public override void Import(IBuildImporterContext context)
        {
            var configurer = (TfsConfigurer)this.GetExtensionConfigurer();

            this.LogDebug("Searching for build {0} for team project {1} definition {2}...", this.TfsBuildNumber, this.TeamProject, this.BuildDefinition);
            var tfsBuild = configurer.GetBuildInfo(this.TeamProject, this.BuildDefinition, this.TfsBuildNumber, this.IncludeUnsuccessful);

            if (tfsBuild == null)
            {
                this.LogError("Query did not return any builds.");
                return;
            }

            this.LogInformation("TFS Build Number: {0}", tfsBuild.BuildNumber);

            if (string.IsNullOrWhiteSpace(tfsBuild.DropLocation))
            {
                this.LogError("TFS configuration error: the selected build definition does not have a drop location specified.");
                return;
            }

            this.LogInformation("Drop location: {0}", tfsBuild.DropLocation);

            using (var agent = Util.Agents.CreateAgentFromId(configurer.ServerId))
            {
                var fileOps = agent.GetService<IFileOperationsExecuter>();

                this.LogDebug("Querying drop location...");
                var directoryResult = Util.Files.GetDirectoryEntry(
                    new GetDirectoryEntryCommand
                    {
                        Path = tfsBuild.DropLocation,
                        Recurse = true,
                        IncludeRootPath = true
                    }
                ).Entry;

                var matches = Util.Files.Comparison.GetMatches(tfsBuild.DropLocation, directoryResult, new[] { "*" })
                    .Where(e => !IsSamePath(e.Path, tfsBuild.DropLocation))
                    .ToList();

                if (!matches.Any())
                {
                    this.LogWarning("No files were found in the drop folder.");
                    return;
                }

                this.LogDebug("Creating {0} artifact...", this.ArtifactName);
                var artifactId = new ArtifactIdentifier(context.ApplicationId, context.ReleaseNumber, context.BuildNumber, context.DeployableId, this.ArtifactName);
                using (var artifact = new ArtifactBuilder(artifactId))
                {
                    artifact.RootPath = tfsBuild.DropLocation;

                    foreach (var match in matches)
                        artifact.Add(match, fileOps);

                    artifact.Commit();
                }
            }

            this.LogDebug("Creating $TfsBuildNumber variable...");
            StoredProcs.Variables_CreateOrUpdateVariableDefinition(
                Variable_Name: "TfsBuildNumber",
                Environment_Id: null,
                Server_Id: null,
                ApplicationGroup_Id: null,
                Application_Id: context.ApplicationId,
                Deployable_Id: null,
                Release_Number: context.ReleaseNumber,
                Build_Number: context.BuildNumber,
                Execution_Id: null,
                Value_Text: tfsBuild.BuildNumber,
                Sensitive_Indicator: YNIndicator.No
            ).Execute();
        }

        private static bool IsSamePath(string path1, string path2)
        {
            if (object.ReferenceEquals(path1, path2))
                return true;
            if (object.ReferenceEquals(path1, null) || object.ReferenceEquals(path2, null))
                return false;

            if (Math.Abs(path1.Length - path2.Length) > 1)
                return false;

            var pathA = path1;
            if (pathA.EndsWith("\\") || pathA.EndsWith("/"))
                pathA = pathA.Substring(0, pathA.Length - 1);
            var pathB = path2;
            if (pathB.EndsWith("\\") || pathB.EndsWith("/"))
                pathB = pathB.Substring(0, pathB.Length - 1);

            return string.Equals(pathA.Replace('/', '\\'), pathB.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
        }
    }
}
