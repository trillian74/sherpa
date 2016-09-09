﻿using Flurl;
using log4net;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.WebParts;
using Newtonsoft.Json.Linq;
using Sherpa.Library.SiteHierarchy.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using File = Microsoft.SharePoint.Client.File;

namespace Sherpa.Library.SiteHierarchy
{
    public class ContentUploadManager
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static Dictionary<string, DateTime> LastUpload = new Dictionary<string, DateTime>();

        private readonly string _contentDirectoryPath;

        public ContentUploadManager(string rootConfigurationPath)
        {
            _contentDirectoryPath = rootConfigurationPath;
        }

        public void UploadFilesInFolder(ClientContext context, Web web, List<ShContentFolder> contentFolders, bool incrementalUpload)
        {
            foreach (ShContentFolder folder in contentFolders)
            {
                UploadFilesInFolder(context, web, folder, incrementalUpload);
            }
        }

        public void UploadFilesInFolder(ClientContext context, Web web, ShContentFolder contentFolder, bool incrementalUpload)
        {
            Log.Info("Uploading files from contentfolder " + contentFolder.FolderName);

            string uploadTargetFolder;
            Folder rootFolder;

            web.Lists.EnsureSiteAssetsLibrary();
            context.Load(web.Lists);
            context.ExecuteQuery();

            if (!string.IsNullOrEmpty(contentFolder.ListUrl))
            {
                context.Load(web, w => w.ServerRelativeUrl);
                context.ExecuteQuery();

                var listUrl = Url.Combine(web.ServerRelativeUrl, contentFolder.ListUrl);
                rootFolder = web.GetFolderByServerRelativeUrl(listUrl);
                context.Load(rootFolder);
                context.ExecuteQuery();

                uploadTargetFolder = Url.Combine(listUrl, contentFolder.FolderUrl);
            }
            else if (!string.IsNullOrEmpty(contentFolder.ListName))
            {
                var assetLibrary = web.Lists.GetByTitle(contentFolder.ListName);
                context.Load(assetLibrary, l => l.Title, l => l.RootFolder);
                context.ExecuteQuery();
                rootFolder = assetLibrary.RootFolder;
                uploadTargetFolder = Url.Combine(assetLibrary.RootFolder.ServerRelativeUrl, contentFolder.FolderUrl);
            }
            else
            {
                Log.ErrorFormat("You need to specify either ListName or ListUrl for the Content Folder {0}", contentFolder.FolderName);
                return;
            }
            //OfficeDevPnP.Core.WebAPI..
            var configRootFolder = Path.Combine(_contentDirectoryPath, contentFolder.FolderName);

            EnsureTargetFolder(context, web, rootFolder.ServerRelativeUrl, contentFolder.FolderUrl, uploadTargetFolder);

            EnsureAllContentFolders(context, web, configRootFolder, uploadTargetFolder);

            List<ShFileProperties> filePropertiesCollection = null;
            if (!string.IsNullOrEmpty(contentFolder.PropertiesFile))
            {
                var propertiesFilePath = Path.Combine(configRootFolder, contentFolder.PropertiesFile);
                var filePersistanceProvider = new FilePersistanceProvider<List<ShFileProperties>>(propertiesFilePath);
                filePropertiesCollection = filePersistanceProvider.Load();
            }

            context.Load(context.Site, site => site.ServerRelativeUrl);
            context.Load(context.Web, w => w.ServerRelativeUrl, w => w.Language);
            context.ExecuteQuery();

            String[] excludedFileExtensions = { };
            if (!string.IsNullOrEmpty(contentFolder.ExcludeExtensions))
            {
                excludedFileExtensions = contentFolder.ExcludeExtensions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            }
            var files = Directory.GetFiles(configRootFolder, "*", SearchOption.AllDirectories)
                .Where(file => !excludedFileExtensions.Contains(Path.GetExtension(file).ToLower())).ToList();

            if (incrementalUpload)
            {
                files = files.Where(f => !LastUpload.ContainsKey(contentFolder.FolderName) || new FileInfo(f).LastWriteTimeUtc > LastUpload[contentFolder.FolderName]).ToList();
            }

            int filesUploaded = 0;
            foreach (string filePath in files)
            {
                UploadAndPublishSingleFile(context, web, configRootFolder, contentFolder, uploadTargetFolder, rootFolder, filePropertiesCollection, filePath);

                filesUploaded++;
            }

            if (filesUploaded == 0)
            {
                Log.Info("No files updated since last upload.");
            }
            else
            {
                Log.InfoFormat("{0} file(s) uploaded", filesUploaded);
            }

            if (LastUpload.ContainsKey(contentFolder.FolderName))
            {
                LastUpload[contentFolder.FolderName] = DateTime.UtcNow;
            }
            else
            {
                LastUpload.Add(contentFolder.FolderName, DateTime.UtcNow);
            }
        }

        private static void EnsureAllContentFolders(ClientContext context, Web web, string configRootFolder,
            string uploadTargetFolder)
        {
            foreach (string folder in Directory.GetDirectories(configRootFolder, "*", SearchOption.AllDirectories))
            {
                var folderName = Url.Combine(uploadTargetFolder, folder.Replace(configRootFolder, "").Replace("\\", "/"));
                if (!web.DoesFolderExists(folderName))
                {
                    web.Folders.Add(folderName);
                }
            }
            context.ExecuteQuery();
        }

        private void EnsureTargetFolder(ClientContext context, Web web, string listUrl, string contentFolderUrl,
            string uploadTargetFolder)
        {
            if (!web.DoesFolderExists(uploadTargetFolder))
            {
                var folderPaths = contentFolderUrl.Split('/');
                var folderUrl = listUrl;
                foreach (var folder in folderPaths)
                {
                    folderUrl = Url.Combine(folderUrl, folder);
                    if (!web.DoesFolderExists(folderUrl))
                    {
                        web.Folders.Add(folderUrl);
                        context.ExecuteQuery();
                    }
                }
            }
        }

        private void UploadAndPublishSingleFile(ClientContext context, Web web, string configRootFolder, ShContentFolder contentFolder, string uploadTargetFolder, Folder rootFolder, List<ShFileProperties> filePropertiesCollection, string filePath)
        {
            var pathToFileFromRootFolder = filePath.Replace(configRootFolder.TrimEnd(new[] { '\\' }) + "\\", "");
            var fileName = Path.GetFileName(pathToFileFromRootFolder);

            pathToFileFromRootFolder = pathToFileFromRootFolder.Replace("\\", "/");

            if (!string.IsNullOrEmpty(contentFolder.PropertiesFile) && contentFolder.PropertiesFile == fileName)
            {
                Log.DebugFormat("Skipping file upload of {0} since it's used as a configuration file", fileName);
                return;
            }

            ShFileProperties fileProperties = null;
            if (filePropertiesCollection != null)
            {
                fileProperties = filePropertiesCollection.SingleOrDefault(f => f.Path == pathToFileFromRootFolder);
            }

            Log.DebugFormat("Uploading file {0} to {1}", fileName, contentFolder.ListUrl);
            var fileUrl = GetFileUrl(uploadTargetFolder, pathToFileFromRootFolder, fileProperties);
            web.CheckOutFile(fileUrl);

            if (fileProperties == null || fileProperties.ReplaceContent)
            {
                var newFile = GetFileCreationInformation(context, fileUrl, filePath, pathToFileFromRootFolder, fileProperties);

                File uploadFile = rootFolder.Files.Add(newFile);

                context.Load(uploadFile);
                context.ExecuteQuery();
            }
            var reloadedFile = web.GetFileByServerRelativeUrl(fileUrl);
            context.Load(reloadedFile);
            context.ExecuteQuery();

            ApplyFileProperties(context, fileProperties, reloadedFile);

            try
            {
                reloadedFile.PublishFileToLevel(FileLevel.Published);
            }
            catch
            {
                Log.Warn("Couldn't publish file " + fileUrl);
            }
        }

        private FileCreationInformation GetFileCreationInformation(ClientContext context, string fileUrl, string filePath, string pathToFileFromRootFolder, ShFileProperties fileProperties)
        {
            var fileCreationInfo = new FileCreationInformation
            {
                Url = fileUrl,
                Overwrite = true,
                Content = System.IO.File.ReadAllBytes(filePath),
            };

            if (fileProperties != null)
            {
                if (fileProperties.ReplaceTokensInTextFile)
                {
                    var fileContents = System.IO.File.ReadAllText(filePath);
                    fileContents = ReplaceTokensInText(fileContents, context);

                    fileCreationInfo.Content = Encoding.UTF8.GetBytes(fileContents);
                }
            }

            return fileCreationInfo;
        }

        private string GetFileUrl(string uploadTargetFolder, string pathToFileFromRootFolder, ShFileProperties fileProperties)
        {
            var fileUrl = Url.Combine(uploadTargetFolder, pathToFileFromRootFolder);

            if (fileProperties != null)
            {
                fileUrl = Url.Combine(uploadTargetFolder, fileProperties.Url);
            }

            return fileUrl;
        }

        private void ApplyFileProperties(ClientContext context, ShFileProperties fileProperties, File uploadFile)
        {
            if (fileProperties != null)
            {
                var filePropertiesWithTokensReplaced = new Dictionary<string, string>();
                foreach (KeyValuePair<string, string> keyValuePair in fileProperties.Properties)
                {
                    filePropertiesWithTokensReplaced.Add(keyValuePair.Key, ReplaceTokensInText(keyValuePair.Value, context));
                }
                uploadFile.SetFileProperties(filePropertiesWithTokensReplaced);

                if (uploadFile.Name.ToLower().EndsWith(".aspx"))
                    AddWebParts(context, uploadFile, fileProperties.WebParts, fileProperties.ReplaceWebParts);
                context.ExecuteQuery();
            }
        }

        public static string ReplaceTokensInText(string valueWithTokens, ClientContext context)
        {
            //Check if we have the context info we need, in which case we don't want to ExecuteQuery
            if (context.Site == null || context.Web == null)
            {
                context.Load(context.Site, site => site.ServerRelativeUrl);
                context.Load(context.Web, web => web.ServerRelativeUrl, web => web.Language);
                context.ExecuteQuery();
            }

            var siteCollectionUrl = context.Site.ServerRelativeUrl == "/" ? string.Empty : context.Site.ServerRelativeUrl;
            var webUrl = context.Web.ServerRelativeUrl == "/" ? string.Empty : context.Web.ServerRelativeUrl;

            return valueWithTokens
                .Replace("~SiteCollection", siteCollectionUrl)
                .Replace("~sitecollection", siteCollectionUrl)
                .Replace("{sitecollection}", siteCollectionUrl)
                .Replace("&#126;SiteCollection", siteCollectionUrl)
                .Replace("&#126;sitecollection", siteCollectionUrl)
                .Replace("~Site", webUrl)
                .Replace("~site", webUrl)
                .Replace("{site}", webUrl)
                .Replace("&#126;Site", webUrl)
                .Replace("&#126;site", webUrl)
                .Replace("$Resources:core,Culture;", new CultureInfo((int)context.Web.Language).Name)
                .Replace("|NewGuid|", Guid.NewGuid().ToString());
        }

        public void AddWebParts(ClientContext context, File uploadFile, List<ShWebPartReference> webPartReferences, bool replaceWebParts)
        {
            // we should be allowed to delete webparts (by using replaceWebparts and define no new ones
            if (webPartReferences.Count <= 0 && !replaceWebParts) return;

            var limitedWebPartManager = uploadFile.GetLimitedWebPartManager(PersonalizationScope.Shared);

            context.Load(limitedWebPartManager, manager => manager.WebParts);
            context.ExecuteQuery();

            if (limitedWebPartManager.WebParts.Count == 0 || replaceWebParts)
            {
                for (int i = limitedWebPartManager.WebParts.Count - 1; i >= 0; i--)
                {
                    limitedWebPartManager.WebParts[i].DeleteWebPart();
                    context.ExecuteQuery();
                }

                foreach (ShWebPartReference webPart in webPartReferences)
                {
                    //Convention: All webparts are located in the content/webparts folder
                    var webPartPath = Path.Combine(_contentDirectoryPath, "webparts", webPart.FileName);
                    var webPartFileContent = System.IO.File.ReadAllText(webPartPath);
                    if (!System.IO.File.Exists(webPartPath))
                    {
                        Log.ErrorFormat("Webpart at path {0} not found", webPartPath);
                        continue;
                    }

                    //Token replacement in the webpart XML
                    webPartFileContent = ReplaceTokensInText(webPartFileContent, context);

                    //Overriding DataProviderJSON properties if specified. Need to use different approach (Update XML directly before import)
                    if (webPart.PropertiesOverrides.Count > 0 || webPart.DataProviderJSONOverrides.Count > 0)
                    {
                        webPartFileContent = ReplaceWebPartPropertyOverrides(context, webPart, webPartFileContent);
                    }

                    var webPartDefinition = limitedWebPartManager.ImportWebPart(webPartFileContent);
                    limitedWebPartManager.AddWebPart(webPartDefinition.WebPart, webPart.ZoneID, webPart.Order);
                    context.Load(limitedWebPartManager);
                    context.ExecuteQuery();
                }
            }
        }

        private string ReplaceWebPartPropertyOverrides(ClientContext context, ShWebPartReference webPart, string webPartcontent)
        {
            XmlReader reader = XmlReader.Create(new StringReader(webPartcontent));
            XElement doc = XElement.Load(reader);
            foreach (KeyValuePair<string, string> propertyOverride in webPart.PropertiesOverrides)
            {
                //Token replacement in the PropertiesOverrides JSON array
                var propOverrideValue = ReplaceTokensInText(propertyOverride.Value, context);
                SetPropertyValueInXmlDocument(doc, propertyOverride.Key, propOverrideValue);
            }
            foreach (KeyValuePair<string, string> keyValuePair in webPart.DataProviderJSONOverrides)
            {
                var propOverrideValue = ReplaceTokensInText(keyValuePair.Value, context);
                SetPropertyValueInXmlDocument(doc, "DataProviderJSON", propOverrideValue, keyValuePair.Key);
            }

            return doc.ToString();
        }

        public static void SetPropertyValueInXmlDocument(XElement doc, string propertyName, string value, string jsonPropertyName = null)
        {
            var element = doc.XPathSelectElement(".//*[local-name() = '" + propertyName + "']") ??
            doc.XPathSelectElement(".//*[local-name() = 'property' and @name='" + propertyName + "']");

            if (element != null)
            {
                if (!string.IsNullOrWhiteSpace(jsonPropertyName))
                {
                    dynamic dp = JObject.Parse(element.Value);
                    dp[jsonPropertyName] = value;
                    value = JObject.FromObject(dp).ToString();
                }
                try
                {
                    element.Value = value;
                }
                catch (Exception e)
                {
                    Log.Error("Could not set web part value of property " + propertyName);
                }
            }
            else
            {
                Log.Error("Could not find web part element " + propertyName);
            }
        }
    }
}