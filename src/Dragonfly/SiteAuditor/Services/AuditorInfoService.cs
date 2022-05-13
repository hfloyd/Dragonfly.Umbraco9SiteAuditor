namespace Dragonfly.SiteAuditor.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Dragonfly.SiteAuditor.Models;
    using Dragonfly.UmbracoServices;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;
    using Umbraco.Cms.Core;
    using Umbraco.Cms.Core.Hosting;
    using Umbraco.Cms.Core.Models;
    using Umbraco.Cms.Core.Models.PublishedContent;
    using Umbraco.Cms.Core.Services;
    using Umbraco.Cms.Core.Web;
    using Umbraco.Cms.Web.Common;
    using Umbraco.Extensions;

    public class AuditorInfoService
    {
        #region Private Vars
        private readonly UmbracoHelper _umbracoHelper;
        private readonly ILogger _logger;

        private readonly IUmbracoContextAccessor _umbracoContextAccessor;
        private readonly IUmbracoContext _umbracoContext;
        private readonly ServiceContext _services;
        private readonly FileHelperService _FileHelperService;
        private readonly HttpContext _Context;
        private readonly IHostingEnvironment _HostingEnvironment;
        private readonly DependencyLoader _Dependencies;

        private bool _HasUmbracoContext;
        #endregion

        #region Public Props
        /// <summary>
        /// Default string used for NodePathAsText
        /// ' » ' unless explicitly changed
        /// </summary>
        public string DefaultDelimiter
        {
            get { return _defaultDelimiter; }
            internal set { _defaultDelimiter = value; }
        }
        private string _defaultDelimiter = " » ";

        #endregion

        public AuditorInfoService(DependencyLoader dependencies, ILogger<SiteAuditorService> logger)
        {
            //Services
            _Dependencies = dependencies;
            _HostingEnvironment = dependencies.HostingEnvironment;
            _umbracoHelper = dependencies.UmbHelper;
            _FileHelperService = dependencies.DragonflyFileHelperService;
            _Context = dependencies.Context;
            _logger = logger;
            _services = dependencies.Services;

            _umbracoContextAccessor = dependencies.UmbracoContextAccessor;
            _HasUmbracoContext = _umbracoContextAccessor.TryGetUmbracoContext(out _umbracoContext);

        }

        /// <summary>
        /// Takes an Umbraco content node and returns the full "path" to it using ancestor Node Names
        /// </summary>
        /// <param name="UmbContentNode">
        /// The Umbraco Content Node.
        /// </param>
        /// <returns>
        /// list of strings representing ancestor node names
        /// </returns>
        public IEnumerable<string> NodePath(IContent UmbContentNode)
        {
            var nodepathList = new List<string>();
            string pathIdsCsv = UmbContentNode.Path;
            string[] pathIdsArray = pathIdsCsv.Split(',');

            foreach (var sId in pathIdsArray)
            {
                if (sId != "-1")
                {
                    IContent getNode = _services.ContentService.GetById(Convert.ToInt32(sId));
                    string nodeName = getNode.Name;
                    nodepathList.Add(nodeName);
                }
            }

            return nodepathList;
        }

        /// <summary>
        /// Takes an Umbraco content node and returns the full "path" to it using ancestor Node Names
        /// </summary>
        /// <param name="Content"></param>
        /// <returns>single concatenated string, using default delimiter</returns>
        public string NodePathAsCustomText(IContent Content)
        {
            var paths = NodePath(Content);
            var nodePath = string.Join(_defaultDelimiter, paths);
            return nodePath;
        }

        /// <summary>
        /// Takes an Umbraco content node and returns the full "path" to it using ancestor Node Names
        /// </summary>
        /// <param name="Content"></param>
        /// <param name="Separator"></param>
        /// <returns>single concatenated string, using provided 'Separator' string as delimiter</returns>
        public string NodePathAsCustomText(IContent Content, string Separator)
        {
            var paths = NodePath(Content);
            var nodePath = string.Join(Separator, paths);
            return nodePath;
        }

        /// <summary>
        /// Get a NodePropertyDataTypeInfo model for a specified Node and Property Alias
        /// (Includes information about the Property, Datatype, and the node's property Value)
        /// </summary>
        /// <param name="PropertyAlias"></param>
        /// <param name="Node">IPublishedContent Node</param>
        /// <returns></returns>
        public NodePropertyDataTypeInfo GetPropertyDataTypeInfo(string PropertyAlias, IPublishedContent PubNode)
        {
            var umbContentService = _services.ContentService;
            var umbContentTypeService = _services.ContentTypeService;
            var umbDataTypeService = _services.DataTypeService;

            var dtInfo = new NodePropertyDataTypeInfo();

            if (PubNode != null)
            {
                dtInfo.NodeId = PubNode.Id;

                //Get Property
                var content = umbContentService.GetById(PubNode.Id);
                dtInfo.Property = content.Properties.First(n => n.Alias == PropertyAlias);
                dtInfo.PropertyData = PubNode.Value(PropertyAlias);

                //Find datatype of property
                IDataType dataType = null;

                var docType = umbContentTypeService.Get(PubNode.ContentType.Id);
                var matchingProperties = docType.PropertyTypes.Where(n => n.Alias == PropertyAlias).ToList();

                if (matchingProperties.Any())
                {
                    var propertyType = matchingProperties.First();
                    dataType = umbDataTypeService.GetDataType(propertyType.DataTypeId);

                    dtInfo.DataType = dataType;
                    dtInfo.PropertyEditorAlias = dataType.EditorAlias;
                    dtInfo.DatabaseType = dataType.DatabaseType.ToString();
                    dtInfo.DocTypeAlias = PubNode.ContentType.Alias;
                }
                else
                {
                    //Look at Compositions for prop data
                    var matchingCompProperties =
                        docType.CompositionPropertyTypes.Where(n => n.Alias == PropertyAlias).ToList();
                    if (matchingCompProperties.Any())
                    {
                        var propertyType = matchingCompProperties.First();
                        dataType = umbDataTypeService.GetDataType(propertyType.DataTypeId);

                        dtInfo.DataType = dataType;
                        dtInfo.PropertyEditorAlias = dataType.EditorAlias;
                        dtInfo.DatabaseType = dataType.DatabaseType.ToString();

                        if (docType.ContentTypeComposition.Any())
                        {
                            var compsList = docType.ContentTypeComposition
                                .Where(n => n.PropertyTypeExists(PropertyAlias)).ToList();
                            if (compsList.Any())
                            {
                                dtInfo.DocTypeAlias = PubNode.ContentType.Alias;
                                dtInfo.DocTypeCompositionAlias = compsList.First().Alias;
                            }
                            else
                            {
                                dtInfo.DocTypeAlias = PubNode.ContentType.Alias;
                                dtInfo.DocTypeCompositionAlias = "Unknown Composition";
                            }
                        }
                    }
                    else
                    {
                        dtInfo.ErrorMessage =
                            $"No property found for alias '{PropertyAlias}' in DocType '{docType.Name}'";
                    }
                }
            }
            else
            {
                _logger.LogError($"AuditorInfoService.GetPropertyDataTypeInfo: PubNode is null");
            }

            return dtInfo;
        }

        /// <summary>
        /// Get a NodePropertyDataTypeInfo model for a specified Node and Property Alias
        /// (Includes information about the Property, Datatype, and the node's property Value)
        /// </summary>
        /// <param name="PropertyAlias"></param>
        /// <param name="Node">IContent Node</param>
        /// <returns></returns>
        public NodePropertyDataTypeInfo GetPropertyDataTypeInfo(string PropertyAlias, IContent ContentNode)
        {
            var umbContentService = _services.ContentService;
            var umbContentTypeService = _services.ContentTypeService;
            var umbDataTypeService = _services.DataTypeService;

            var dtInfo = new NodePropertyDataTypeInfo();

            if (ContentNode != null)
            {
                dtInfo.NodeId = ContentNode.Id;

                //Get Property
                //var content = umbContentService.GetById(Node.Id);
                dtInfo.Property = ContentNode.Properties.First(n => n.Alias == PropertyAlias);
                dtInfo.PropertyData = ContentNode.GetValue(PropertyAlias);

                //Find datatype of property
                IDataType dataType = null;

                var docType = umbContentTypeService.Get(ContentNode.ContentType.Id);
                var matchingProperties = docType.PropertyTypes.Where(n => n.Alias == PropertyAlias).ToList();

                if (matchingProperties.Any())
                {
                    var propertyType = matchingProperties.First();
                    dataType = umbDataTypeService.GetDataType(propertyType.DataTypeId);

                    dtInfo.DataType = dataType;
                    dtInfo.PropertyEditorAlias = dataType.EditorAlias;
                    dtInfo.DatabaseType = dataType.DatabaseType.ToString();
                    dtInfo.DocTypeAlias = ContentNode.ContentType.Alias;
                }
                else
                {
                    //Look at Compositions for prop data
                    var matchingCompProperties =
                        docType.CompositionPropertyTypes.Where(n => n.Alias == PropertyAlias).ToList();
                    if (matchingCompProperties.Any())
                    {
                        var propertyType = matchingCompProperties.First();
                        dataType = umbDataTypeService.GetDataType(propertyType.DataTypeId);

                        dtInfo.DataType = dataType;
                        dtInfo.PropertyEditorAlias = dataType.EditorAlias;
                        dtInfo.DatabaseType = dataType.DatabaseType.ToString();

                        if (docType.ContentTypeComposition.Any())
                        {
                            var compsList = docType.ContentTypeComposition
                                .Where(n => n.PropertyTypeExists(PropertyAlias)).ToList();
                            if (compsList.Any())
                            {
                                dtInfo.DocTypeAlias = ContentNode.ContentType.Alias;
                                dtInfo.DocTypeCompositionAlias = compsList.First().Alias;
                            }
                            else
                            {
                                dtInfo.DocTypeAlias = ContentNode.ContentType.Alias;
                                dtInfo.DocTypeCompositionAlias = "Unknown Composition";
                            }
                        }
                    }
                    else
                    {
                        dtInfo.ErrorMessage =
                            $"No property found for alias '{PropertyAlias}' in DocType '{docType.Name}'";
                    }
                }
            }

            return dtInfo;
        }


    }
}
