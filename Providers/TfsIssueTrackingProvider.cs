﻿using System;
using System.Linq;
using System.Net;
using Inedo.BuildMaster;
using Inedo.BuildMaster.Extensibility.Providers;
using Inedo.BuildMaster.Extensibility.Providers.IssueTracking;
using Inedo.BuildMaster.Web;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace Inedo.BuildMasterExtensions.TFS
{
    /// <summary>
    /// Connects to a Team Foundation 2012 Server to integrate issue tracking with BuildMaster.
    /// </summary>
    [ProviderProperties(
        "Team Foundation Server (2012)",
        "Supports TFS 2012 and earlier; requires that Visual Studio Team System (or greater) 2012 is installed.",
        RequiresTransparentProxy = true)]
    [CustomEditor(typeof(TfsIssueTrackingProviderEditor))]
    public class TfsIssueTrackingProvider : IssueTrackingProviderBase, ICategoryFilterable, IUpdatingProvider
    {
        /// <summary>
        /// Initializes a new instance of the Tfs2012IssueTrackingProvider class.
        /// </summary>
        public TfsIssueTrackingProvider()
        {
        }
        /// <summary>
        /// The base URL of the TFS store, should include collection name, e.g. "http://server:port/tfs"
        /// </summary>
        [Persistent]
        public string BaseUrl { get; set; }
        /// <summary>
        /// Indicates the full name of the custom field that contains the release number associated with the work item
        /// </summary>
        [Persistent]
        public string CustomReleaseNumberFieldName { get; set; }
        /// <summary>
        /// The username used to connect to the server
        /// </summary>
        [Persistent]
        public string UserName { get; set; }
        /// <summary>
        /// The password used to connect to the server
        /// </summary>
        [Persistent]
        public string Password { get; set; }
        /// <summary>
        /// The domain of the server
        /// </summary>
        [Persistent]
        public string Domain { get; set; }
        /// <summary>
        /// Returns true if BuildMaster should connect to TFS using its own account, false if the credentials are specified
        /// </summary>
        [Persistent]
        public bool UseSystemCredentials { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether to allow HTML issue descriptions.
        /// </summary>
        [Persistent]
        public bool AllowHtmlIssueDescriptions { get; set; }

        public bool CanAppendIssueDescriptions
        {
            get { return true; }
        }
        public bool CanChangeIssueStatuses
        {
            get { return true; }
        }
        public bool CanCloseIssues
        {
            get { return true; }
        }
        public string[] CategoryIdFilter { get; set; }
        public string[] CategoryTypeNames
        {
            get { return new[] { "Collection", "Project", "Area Path" }; }
        }

        /// <summary>
        /// Gets the base URI of the Team Foundation Server
        /// </summary>
        protected Uri BaseUri
        {
            get { return new Uri(BaseUrl); }
        }

        /// <summary>
        /// Gets a URL to the specified issue.
        /// </summary>
        /// <param name="issue">The issue whose URL is returned.</param>
        /// <returns>
        /// The URL of the specified issue if applicable; otherwise null.
        /// </returns>
        public override string GetIssueUrl(IssueTrackerIssue issue)
        {
            using (var teamProjectCollection = this.GetTeamProjectCollection())
            {
                var hyperlinkService = teamProjectCollection.GetService<TswaClientHyperlinkService>();
                var workItemUrl = hyperlinkService.GetWorkItemEditorUrl(Convert.ToInt32(issue.IssueId)).AbsoluteUri;

                return workItemUrl;
            }
        }
        /// <summary>
        /// Gets an array of <see cref="Issue"/> objects that are for the current
        /// release
        /// </summary>
        /// <param name="releaseNumber">The release number from which the issues should be retrieved</param>
        public override IssueTrackerIssue[] GetIssues(string releaseNumber)
        {
            bool filterByProject = CategoryIdFilter.Length >= 2 && !string.IsNullOrEmpty(CategoryIdFilter[1]);
            bool filterByAreaPath = CategoryIdFilter.Length == 3 && !string.IsNullOrEmpty(CategoryIdFilter[2]);

            using (var tfs = this.GetTeamProjectCollection())
            {
                var workItems = GetWorkItemCollection(
                    tfs.GetService<WorkItemStore>(),
                    @"SELECT [System.ID], 
                    [System.Title], 
                    [System.Description], 
                    [System.State],
                    [System.AreaPath] 
                    {0}
                    FROM WorkItems 
                    {1}
                    {2}
                    {3}
                    ORDER BY [System.ID] ASC",
                    string.IsNullOrEmpty(this.CustomReleaseNumberFieldName)
                        ? ""
                        : ", [" + this.CustomReleaseNumberFieldName + "]",
                    string.IsNullOrEmpty(this.CustomReleaseNumberFieldName)
                        ? ""
                        : string.Format("WHERE [{0}] = '{1}'", this.CustomReleaseNumberFieldName, releaseNumber),
                    filterByProject
                        ? string.Format("{1} [System.TeamProject] = '{0}'", CategoryIdFilter[1], string.IsNullOrEmpty(this.CustomReleaseNumberFieldName) ? "WHERE" : "AND")
                        : "",
                    filterByAreaPath
                        ? string.Format("AND [System.AreaPath] under '{0}'", CategoryIdFilter[2])
                        : ""
                );

                // transform work items returned by SDK into BuildMaster's issues array type
                return workItems
                    .Cast<WorkItem>()
                    .Select(wi => new TfsIssue(wi, this.CustomReleaseNumberFieldName, this.AllowHtmlIssueDescriptions))
                    .Where(wi => wi.ReleaseNumber == releaseNumber)
                    .ToArray();
            }
        }
        /// <summary>
        /// Determines if the specified issue is closed
        /// </summary>
        /// <param name="issue">The issue to determine closed status</param>
        public override bool IsIssueClosed(IssueTrackerIssue issue)
        {
            return issue.IssueStatus == TfsIssue.DefaultStatusNames.Closed || issue.IssueStatus == TfsIssue.DefaultStatusNames.Resolved;
        }
        /// <summary>
        /// Indicates whether the provider is installed and available for use in the current execution context
        /// </summary>
        public override bool IsAvailable()
        {
            try
            {
                typeof(TfsConfigurationServer).GetType();
                return true;
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// Attempts to connect with the current configuration and, if not successful, throws a <see cref="NotAvailableException"/>
        /// </summary>
        public override void ValidateConnection()
        {
            try
            {
                using (var tfs = this.GetTeamProjectCollection())
                {
                }
            }
            catch (Exception ex)
            {
                throw new NotAvailableException(ex.Message, ex);
            }
        }
        public override string ToString()
        {
            return "Connects to a TFS 2012 server to integrate with work items.";
        }
        /// <summary>
        /// Returns an array of all appropriate categories defined within the provider
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// The nesting level (i.e. <see cref="CategoryBase.SubCategories"/>) can never be less than
        /// the length of <see cref="CategoryTypeNames"/>
        /// </remarks>
        public IssueTrackerCategory[] GetCategories()
        {
            // transform collection names from TFS SDK format to BuildMaster's CategoryBase object
            using (var tfs = this.GetTeamProjectCollection())
            {
                return tfs.ConfigurationServer
                    .GetService<ITeamProjectCollectionService>()
                    .GetCollections()
                    .Select(teamProject => TfsCategory.CreateCollection(teamProject, GetProjectCategories(teamProject)))
                    .ToArray();
            }
        }
        /// <summary>
        /// Appends the specified text to the specified issue
        /// </summary>
        /// <param name="issueId">The issue to append to</param>
        /// <param name="textToAppend">The text to append to the issue</param>
        public void AppendIssueDescription(string issueId, string textToAppend)
        {
            using (var tfs = this.GetTeamProjectCollection())
            {
                var workItem = GetWorkItemByID(tfs.GetService<WorkItemStore>(), issueId);
                workItem.PartialOpen();
                workItem.Description += Environment.NewLine + textToAppend;
                workItem.Save();
                workItem.Close();
            }
        }
        /// <summary>
        /// Changes the specified issue's status
        /// </summary>
        /// <param name="issueId">The issue whose status will be changed</param>
        /// <param name="newStatus">The new status text</param>
        public void ChangeIssueStatus(string issueId, string newStatus)
        {
            using (var tfs = this.GetTeamProjectCollection())
            {
                var workItem = GetWorkItemByID(tfs.GetService<WorkItemStore>(), issueId);
                workItem.State = newStatus;

                workItem.Save();
            }
        }
        /// <summary>
        /// Closes the specified issue
        /// </summary>
        /// <param name="issueId">The specified issue to be closed</param>
        public void CloseIssue(string issueId)
        {
            ChangeIssueStatus(issueId, TfsIssue.DefaultStatusNames.Closed);
        }

        protected virtual TfsTeamProjectCollection GetTeamProjectCollection()
        {
            if (this.UseSystemCredentials)
            {
                var projectCollection = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(this.BaseUri);
                projectCollection.EnsureAuthenticated();
                return projectCollection;
            }
            else
            {
                var projectColleciton = new TfsTeamProjectCollection(this.BaseUri, new TfsClientCredentials(new WindowsCredential(new NetworkCredential(this.UserName, this.Password, this.Domain))));
                projectColleciton.EnsureAuthenticated();
                return projectColleciton;
            }
        }

        /// <summary>
        /// Gets the work item by its ID.
        /// </summary>
        /// <param name="workItemID">The work item ID.</param>
        private WorkItem GetWorkItemByID(WorkItemStore store, string workItemID)
        {
            var wiql = String.Format(@"SELECT [System.ID], 
                                [System.Title], 
                                [System.Description], 
                                [System.State] 
                                {0} 
                            FROM WorkItems 
                            WHERE [System.ID] = '{1}'",
                            String.IsNullOrEmpty(this.CustomReleaseNumberFieldName)
                             ? ""
                             : ", [" + this.CustomReleaseNumberFieldName + "]",
                            workItemID);
            var workItemCollection = store.Query(wiql);

            if (workItemCollection.Count == 0) throw new Exception("There is no work item with the ID: " + workItemID);
            if (workItemCollection.Count > 1) throw new Exception("There are multiple issues with the same ID: " + workItemID);

            return workItemCollection[0];
        }
        /// <summary>
        /// Gets a work item collection.
        /// </summary>
        /// <param name="wiqlQueryFormat">The WIQL query format string</param>
        /// <param name="args">The arguments for the format string</param>
        private WorkItemCollection GetWorkItemCollection(WorkItemStore store, string wiqlQueryFormat, params object[] args)
        {
            var wiql = string.Format(wiqlQueryFormat, args);

            return store.Query(wiql);
        }
        /// <summary>
        /// Gets the project categories.
        /// </summary>
        /// <param name="teamProject">The team project collection which houses the project.</param>
        private TfsCategory[] GetProjectCategories(TeamProjectCollection teamProject)
        {
            using (var tfs = this.GetTeamProjectCollection())
            {
                // transform project names from TFS SDK format to BuildMaster's category object
                return tfs.ConfigurationServer
                    .GetTeamProjectCollection(teamProject.Id)
                    .GetService<WorkItemStore>()
                    .Projects
                    .Cast<Project>()
                    .Select(project => TfsCategory.CreateProject(project))
                    .ToArray();
            }
        }
    }
}
