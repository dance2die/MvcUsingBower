﻿#region licence
// ======================================================================================
// Mvc5UsingBower - An example+library to allow an MVC project to use Bower and Grunt
// Filename: BundlerForBower.cs
// Date Created: 2016/02/17
// 
// Under the MIT License (MIT)
// 
// Written by Jon Smith : GitHub JonPSmith, www.thereformedprogrammer.net
// ======================================================================================
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using B4BCore.Internal;

namespace B4BCore
{
    /// <summary>
    /// Used to say whether we are delivering CSS or JavaScript bundle
    /// </summary>
    public enum CssOrJs { Css, Js }

    /// <summary>
    /// This class contains extention methods to produce html strings to include CSS or JavaScript files in a HTML page
    /// </summary>
    public class BundlerForBower
    {
        internal const string B4BConfigFileName = "BundlerForBower.json";
        private readonly string _jsonDataDir;
        private readonly Func<string, string> _getActualFilePathFromVirtualPath;

        private readonly Func<string, string> _getContentUrl;
        private readonly RelPathSearcher _searcher;

        /// <summary>
        /// This creates the class ready for bundling
        /// </summary>
        /// <param name="getContentUrl">This is a function which given a path relative to the MVC project and will
        /// return a http: url to use in the web site. In MVC5 this is provided by urlHelper.Content</param>
        /// <param name="getActualFilePathFromVirtualPath">This is a function which given a path relative to the MVC project 
        /// will return the absolute path. In MVC5 this is provided by System.Web.Hosting.HostingEnvironment.MapPath</param>
        /// <param name="jsonDataDir">The absolute directory path to the folder that holds the BowerBundles.json and the 
        /// optional BundlerForBower.json config file</param>
        public BundlerForBower(Func<string, string> getContentUrl, Func<string, string> getActualFilePathFromVirtualPath, string jsonDataDir)
        {
            _getContentUrl = getContentUrl;
            _getActualFilePathFromVirtualPath = getActualFilePathFromVirtualPath;
            _jsonDataDir = jsonDataDir;
            _searcher = new RelPathSearcher(_getActualFilePathFromVirtualPath);
        }

        /// <summary>
        /// This returns the appropriate html string to include in the web page such that the requested bundle will be loaded.
        /// </summary>
        /// <param name="bundleName"></param>
        /// <param name="cssOrJs"></param>
        /// <param name="inDevelopment">This controls whether we supply individual files or development mode 
        /// or single minified files/CDNs in non-development mode</param>
        /// <returns></returns>
        public string CalculateHtmlIncludes(string bundleName, CssOrJs cssOrJs, bool inDevelopment)
        {
            var config = ConfigInfo.ReadConfig(Path.Combine(_jsonDataDir, B4BConfigFileName));
            var settingFilePath = Path.Combine(_jsonDataDir, config.BundlesFileName);
            var reader = new ReadBundleFile(settingFilePath);

            var fileTypeInfo = config.GetFileTypeData(cssOrJs);

            if (inDevelopment)
            {
                var sb = new StringBuilder();
                //we send the individual files as found in the bundle json file
                foreach (var relFilePath in _searcher.UnpackBundle(reader.GetBundleDebugFiles(bundleName)))
                {
                    sb.AppendLine(fileTypeInfo.DebugHtmlFormatString
                        .Replace(FileTypeConfigInfo.FileUrlParam, _getContentUrl(relFilePath)));
                }
                return sb.ToString();
            }
            
            //We are in nonDebug, i.e. production mode
            var cdnLinks = reader.GetBundleCdnInfo(bundleName);

            return cdnLinks.Any() 
                ? FormCdnIncludes(cdnLinks, bundleName, cssOrJs, fileTypeInfo) 
                : FormSingleMinifiedFileInclude(bundleName, cssOrJs, fileTypeInfo);
        }

        private string FormCdnIncludes(IEnumerable<CdnInfo> cdnLinks, string bundleName, CssOrJs cssOrJs, FileTypeConfigInfo fileTypeInfo)
        {
            if (string.IsNullOrEmpty(fileTypeInfo.CdnHtmlFormatString))
                throw new InvalidOperationException(
                    $"The bundle {bundleName} contains a cdn definition, but the current config does not support CDN for {cssOrJs}");

            var sb = new StringBuilder();
            //we send the individual files as found in the bundle json file
            foreach (var cdnLink in cdnLinks)
            {
                var relFilePath = $"~/{fileTypeInfo.Directory}{cdnLink.Production}";
                var httpFileUrl = _getContentUrl(relFilePath);
                sb.AppendLine(cdnLink.BuildCdnIncludeString(fileTypeInfo.CdnHtmlFormatString, httpFileUrl,
                    () => GetChecksum(_getActualFilePathFromVirtualPath(relFilePath))));
            }
            return sb.ToString();
        }

        //------------------------------------------------------------------
        //private methods

        private string FormSingleMinifiedFileInclude(string bundleName, CssOrJs cssOrJs, FileTypeConfigInfo fileTypeInfo)
        {
            var relFilePath = $"~/{fileTypeInfo.Directory}{bundleName}.min.{cssOrJs.ToString().ToLowerInvariant()}";
            var fileUrl = _getContentUrl(relFilePath);
            var htmlLink = fileTypeInfo.NonDebugHtmlFormatString.Replace(FileTypeConfigInfo.FileUrlParam, fileUrl);
            if (fileTypeInfo.NonDebugHtmlFormatString.Contains(FileTypeConfigInfo.CachebusterParam))
            {
                //I use a SHA256 Hash instead of the file datetime as it allows you to use the general Grunt 'build'
                //command, which rebuilds everything, and the cache buster won't change unless the content changes.
                var cacheBusterValue = GetChecksum(_getActualFilePathFromVirtualPath(relFilePath));
                htmlLink = htmlLink.Replace(FileTypeConfigInfo.CachebusterParam, cacheBusterValue);
            }
            return htmlLink;
        }

        private static string GetChecksum(string file)
        {
            using (var stream = File.OpenRead(file))
            {
                var sha = new SHA256Managed();
                byte[] checksum = sha.ComputeHash(stream);
                var base64 = Convert.ToBase64String(checksum);
                return base64.Replace("/", "_").Replace("+", "-").Substring(0, base64.Length - 1);    //make valid HTTP parameter string
            }
        }
    }
}