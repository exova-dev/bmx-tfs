﻿using Inedo.BuildMaster;
using Inedo.BuildMaster.Data;
using Inedo.BuildMaster.Extensibility.Recipes;
using Inedo.BuildMaster.Web;

namespace Inedo.BuildMasterExtensions.TFS
{
    [RecipeProperties(
       "Deploy TFS Team Build",
       "An application that imports a build artifact from a TFS build's drop location and deploys through multiple environments.",
       RecipeScopes.NewApplication)]
    [CustomEditor(typeof(DeployTeamBuildRecipeEditor))]
    public sealed class DeployTeamBuildRecipe : RecipeBase, IApplicationCreatingRecipe, IWorkflowCreatingRecipe
    {
        public string ApplicationGroup { get; set; }
        public string ApplicationName { get; set; }
        public int ApplicationId { get; set; }

        public string WorkflowName { get; set; }
        public int[] WorkflowSteps { get; set; }
        public int WorkflowId { get; set; }

        public string TargetDeploymentPath { get; set; }
        public string TeamProject { get; set; }
        public string BuildDefinition { get; set; }

        public override void Execute()
        {
            int deployableId = Util.Recipes.CreateDeployable(this.ApplicationId, this.ApplicationName);
            string deployableName = this.TeamProject;
            int firstEnvironmentId = this.WorkflowSteps[0];

            var template = new BuildImporter.TfsBuildImporterTemplate
            {
                BuildDefinition = this.BuildDefinition,
                TeamProject = this.TeamProject
            };

            Util.Recipes.CreateBuildStepBuildImporter(this.WorkflowId, template);

            int firstDeploymentPlanId = Util.Recipes.CreateDeploymentPlanForWorkflowStep(this.WorkflowId, 1);

            int actionGroupId = Util.Recipes.CreateDeploymentPlanActionGroup(
                firstDeploymentPlanId,
                deployableId: null,
                deployableName: null,
                name: "Stop Application",
                description: "Stop/shutdown/disable the application or application servers prior to deployment."
            );

            actionGroupId = Util.Recipes.CreateDeploymentPlanActionGroup(
                firstDeploymentPlanId,
                deployableId: deployableId,
                deployableName: null,
                name: "Deploy " + deployableName,
                description: "Deploy the artifacts created in the build actions, and then any configuration files needed."
            );

            Util.Recipes.AddAction(actionGroupId, 1, Util.Recipes.Munging.MungeCoreExAction(
                "Inedo.BuildMaster.Extensibility.Actions.Artifacts.DeployArtifactAction", new
                {
                    ArtifactName = this.TeamProject,
                    OverriddenTargetDirectory = this.TargetDeploymentPath,
                    DoNotClearTargetDirectory = false
                })
            );

            Util.Recipes.CreateDeploymentPlanActionGroup(
                firstDeploymentPlanId,
                deployableId: null,
                deployableName: null,
                name: "Start Application",
                description: "Start the application or application servers after deployment, and possibly run some post-startup automated testing."
            );


            Util.Recipes.CreateSetupRelease(this.ApplicationId, Domains.ReleaseNumberSchemes.MajorMinor, this.WorkflowId, new[] { deployableId });
        }
    }
}
