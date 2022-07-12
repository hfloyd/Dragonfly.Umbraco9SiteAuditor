namespace Dragonfly.SiteAuditor.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Dragonfly.SiteAuditor.Models;
    using Dragonfly.UmbracoServices;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Microsoft.AspNetCore.Http;
    using Umbraco.Cms.Core;
    using Umbraco.Cms.Core.Hosting;
    using Umbraco.Cms.Core.Models;
    using Umbraco.Cms.Core.Services;
    using Umbraco.Cms.Core.Models.PublishedContent;
    using Umbraco.Cms.Core.PropertyEditors;
    using Umbraco.Cms.Core.Web;
    using Umbraco.Cms.Web.Common;
    using Umbraco.Extensions;

    public class SiteAuditorService
    {
        #region Dependencies
        private readonly UmbracoHelper _umbracoHelper;
        private readonly ILogger _logger;
        private readonly IUmbracoContextAccessor _umbracoContextAccessor;
        private readonly IUmbracoContext _umbracoContext;
        private readonly ServiceContext _services;
        private readonly FileHelperService _FileHelperService;
        private readonly HttpContext _Context;
        private readonly IHostingEnvironment _HostingEnvironment;
        private readonly DependencyLoader _Dependencies;

        private readonly AuditorInfoService _auditorInfoService;

        private bool _HasUmbracoContext;
        #endregion

        #region Private & Internal Variables

        private IEnumerable<AuditableContent> _AllAuditableContent = new List<AuditableContent>();

        private IEnumerable<IContent> _AllContent = new List<IContent>();

        /// <summary>
        /// Use .GetAllCompositions() to return this list
        /// </summary>
        private IEnumerable<IContentTypeComposition> _AllContentTypeComps = new List<IContentType>();

        internal static string DataPath()
        {
            //var config = Config.GetConfig();
            //return config.GetDataPath();

            return "/App_Data/DragonflySiteAuditor/";
        }

        internal static string PluginPath()
        {
            //var config = Config.GetConfig();
            //return config.GetDataPath();

            return "~/App_Plugins/Dragonfly.SiteAuditor/";
        }

        #endregion

        #region Public Properties
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

        #region ctor

        public SiteAuditorService(DependencyLoader dependencies, ILogger<SiteAuditorService> logger, AuditorInfoService auditorInfoService)
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

            _auditorInfoService = auditorInfoService;
        }

        //public SiteAuditorService(UmbracoHelper UmbHelper, UmbracoContext UmbContext, ServiceContext Services, ILogger Logger)
        //{
        //    //Services
        //    _umbracoHelper = UmbHelper;
        //    _services = Services;
        //    _logger = Logger;
        //    _umbracoContext = UmbContext;
        //    _HasUmbracoContext = true;
        //}

        #endregion

        #region All Nodes (IPublishedContent)

        /// <summary>
        /// Gets all site nodes as IPublishedContent
        /// </summary>
        /// <param name="IncludeUnpublished">Should unpublished nodes be included? (They will be returned as 'virtual' IPublishedContent models)</param>
        /// <returns></returns>
        public IEnumerable<IPublishedContent> GetAllNodes()
        {
            var nodesList = new List<IPublishedContent>();

            //if (IncludeUnpublished)
            //{
            //    //Get nodes as IContent
            //    var topLevelNodes = _services.ContentService.GetRootContent().OrderBy(n => n.SortOrder);

            //    foreach (var thisNode in topLevelNodes)
            //    {
            //        nodesList.AddRange(LoopNodes(thisNode));
            //    }
            //}
            //else
            //{
            //Get nodes as IPublishedContent
            var topLevelNodes = _umbracoHelper.ContentAtRoot().OrderBy(n => n.SortOrder);

            foreach (var thisNode in topLevelNodes)
            {
                nodesList.AddRange(LoopNodes(thisNode));
            }
            // }

            return nodesList;
        }

        private IEnumerable<IPublishedContent> LoopNodes(IPublishedContent ThisNode)
        {
            var nodesList = new List<IPublishedContent>();

            //Add current node, then loop for children
            try
            {
                nodesList.Add(ThisNode);

                if (ThisNode.Children().Any())
                {
                    foreach (var childNode in ThisNode.Children().OrderBy(n => n.SortOrder))
                    {
                        nodesList.AddRange(LoopNodes(childNode));
                    }
                }
            }
            catch (Exception e)
            {
                //skip
            }

            return nodesList;
        }

        //private IEnumerable<IPublishedContent> LoopNodes(IContent ThisNode)
        //{
        //    var nodesList = new List<IPublishedContent>();

        //    //Add current node, then loop for children
        //    try
        //    {
        //        nodesList.Add(ThisNode.ToPublishedContent());

        //        if (ThisNode.Children().Any())
        //        {
        //            foreach (var childNode in ThisNode.Children().OrderBy(n => n.SortOrder))
        //            {
        //                nodesList.AddRange(LoopNodes(childNode));
        //            }
        //        }
        //    }
        //    catch (Exception e)
        //    {
        //        //skip
        //    }

        //    return nodesList;
        //}

        #endregion

        #region Content 

        public List<IContent> GetAllContent()
        {
            var nodesList = new List<IContent>();

            var topLevelContentNodes = _services.ContentService.GetRootContent().OrderBy(n => n.SortOrder);

            foreach (var thisNode in topLevelContentNodes)
            {
                nodesList.AddRange(LoopForContentNodes(thisNode));
            }

            _AllContent = nodesList;
            return nodesList;
        }

        internal List<IContent> LoopForContentNodes(IContent ThisNode)
        {
            var nodesList = new List<IContent>();

            //Add current node, then loop for children
            nodesList.Add(ThisNode);

            //figure out num of children
            long countChildren;
            var test = _services.ContentService.GetPagedChildren(ThisNode.Id, 0, 1, out countChildren);
            if (countChildren > 0)
            {
                long countTest;
                var allChildren = _services.ContentService.GetPagedChildren(ThisNode.Id, 0, Convert.ToInt32(countChildren), out countTest);
                foreach (var childNode in allChildren.OrderBy(n => n.SortOrder))
                {
                    nodesList.AddRange(LoopForContentNodes(childNode));
                }
            }

            return nodesList;
        }
        #endregion

        #region AuditableContent

        /// <summary>
        /// Gets all site nodes as AuditableContent models
        /// </summary>
        /// <returns></returns>
        public List<AuditableContent> GetContentNodes()
        {
            if (_AllAuditableContent.Any())
            {
                return _AllAuditableContent.ToList();
            }

            var nodesList = new List<AuditableContent>();

            var topLevelContentNodes = _services.ContentService.GetRootContent().OrderBy(n => n.SortOrder);

            foreach (var thisNode in topLevelContentNodes)
            {
                nodesList.AddRange(LoopForAuditableContentNodes(thisNode));
            }

            _AllAuditableContent = nodesList;
            return nodesList;
        }

        /// <summary>
        /// Gets all descendant nodes from provided Root Node Id as AuditableContent models
        /// </summary>
        /// <param name="RootNodeId">Integer Id of Root Node</param>
        /// <returns></returns>
        public List<AuditableContent> GetContentNodes(int RootNodeId)
        {
            var nodesList = new List<AuditableContent>();

            var topLevelNodes = _services.ContentService.GetByIds(RootNodeId.AsEnumerableOfOne()).OrderBy(n => n.SortOrder);

            foreach (var thisNode in topLevelNodes)
            {
                nodesList.AddRange(LoopForAuditableContentNodes(thisNode));
            }

            return nodesList;
        }

        /// <summary>
        /// Gets all descendant nodes from provided Root Node Id as AuditableContent models
        /// </summary>
        /// <param name="RootNodeUdi">Udi of Root Node</param>
        /// <returns></returns>
        public List<AuditableContent> GetContentNodes(Udi RootNodeUdi)
        {
            var nodesList = new List<AuditableContent>();

            //var TopLevelNodes = umbHelper.ContentAtRoot();
            var topLevelNodes = _services.ContentService.GetByIds(RootNodeUdi.AsEnumerableOfOne()).OrderBy(n => n.SortOrder);

            foreach (var thisNode in topLevelNodes)
            {
                nodesList.AddRange(LoopForAuditableContentNodes(thisNode));
            }

            return nodesList;
        }

        public List<AuditableContent> GetContentNodes(string DocTypeAlias)
        {
            var nodesList = new List<AuditableContent>();

            IEnumerable<IContent> allContent;
            if (_AllContent.Any())
            {
                allContent = _AllContent.ToList();
            }
            else
            {
                allContent = GetAllContent().ToList();
            }

            var filteredContent = allContent.Where(n => n.ContentType.Alias == DocTypeAlias);

            foreach (var content in filteredContent)
            {
                nodesList.Add(ConvertIContentToAuditableContent(content));
            }

            return nodesList;
        }

        public List<AuditableContent> GetContentWithProperty(string PropertyAlias)
        {
            var allContent = GetAllContent();
            var contentWithProp = allContent.Where(n => n.HasProperty(PropertyAlias)).ToList();

            return ConvertIContentToAuditableContent(contentWithProp);
        }

        internal List<AuditableContent> LoopForAuditableContentNodes(IContent ThisNode)
        {
            var nodesList = new List<AuditableContent>();

            //Add current node, then loop for children
            AuditableContent auditContent = ConvertIContentToAuditableContent(ThisNode);
            nodesList.Add(auditContent);

            //figure out num of children
            long countChildren;
            var test = _services.ContentService.GetPagedChildren(ThisNode.Id, 0, 1, out countChildren);
            if (countChildren > 0)
            {
                long countTest;
                var allChildren = _services.ContentService.GetPagedChildren(ThisNode.Id, 0, Convert.ToInt32(countChildren), out countTest);
                foreach (var childNode in allChildren.OrderBy(n => n.SortOrder))
                {
                    nodesList.AddRange(LoopForAuditableContentNodes(childNode));
                }
            }

            return nodesList;
        }

        private AuditableContent ConvertIContentToAuditableContent(IContent ThisIContent)
        {
            var ac = new AuditableContent();

            ac.UmbContentNode = ThisIContent;
            ac.IsPublished = ac.UmbContentNode.Published;

            if (ThisIContent.TemplateId != null)
            {
                var template = _services.FileService.GetTemplate((int)ThisIContent.TemplateId);
                ac.TemplateAlias = template.Alias;
            }
            else
            {
                ac.TemplateAlias = "NONE";
            }

            if (ac.UmbContentNode.Published)
            {
                try
                {
                    var iPub = _umbracoHelper.Content(ThisIContent.Id);
                    ac.UmbPublishedNode = iPub;
                    //ac.RelativeNiceUrl = iPub.Url(mode: UrlMode.Relative);
                    //ac.FullNiceUrl = iPub.Url(mode: UrlMode.Absolute);
                }
                catch (Exception e)
                {
                    //Get preview - unpublished
                    var iPub = _umbracoContext.Content.GetById(true, ThisIContent.Id);
                    ac.UmbPublishedNode = iPub;
                }

                if (ac.UmbPublishedNode != null && ac.UmbPublishedNode.ItemType == PublishedItemType.Element)
                {
                    _logger.LogError($"SiteAuditorService.ConvertIContentToAuditableContent: Invalid Item Type (Element) on Node #{ac.UmbContentNode.Id} - Document Type = {ac.UmbContentNode.ContentType.Name}");
                }
                else
                {
                    try
                    {
                        ac.RelativeNiceUrl = ac.UmbPublishedNode != null ? ac.UmbPublishedNode.Url(mode: UrlMode.Relative) : "UNPUBLISHED";
                        ac.FullNiceUrl = ac.UmbPublishedNode != null ? ac.UmbPublishedNode.Url(mode: UrlMode.Absolute) : "UNPUBLISHED";
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(e, $"SiteAuditorService.ConvertIContentToAuditableContent: Unable to set Urls on Node #{ac.UmbContentNode.Id}");
                    }
                }
            }
            
            ac.NodePath = _auditorInfoService.NodePath(ThisIContent);
  
            return ac;
        }

        private List<AuditableContent> ConvertIContentToAuditableContent(List<IContent> ContentList)
        {
            var nodesList = new List<AuditableContent>();
            foreach (var content in ContentList)
            {
                nodesList.Add(ConvertIContentToAuditableContent(content));
            }
            return nodesList;
        }

        private AuditableContent ConvertIPubContentToAuditableContent(IPublishedContent PubContentNode)
        {
            var ac = new AuditableContent();
            ac.UmbContentNode = _services.ContentService.GetById(PubContentNode.Id);
            ac.UmbPublishedNode = PubContentNode;
            ac.NodePath = _auditorInfoService.NodePath(ac.UmbContentNode);
            ac.TemplateAlias = GetTemplateAlias(ac.UmbContentNode);
            //this.DocTypes = new List<string>();

            return ac;
        }

        #endregion



        #region DocTypes

        /// <summary>
        /// Gets list of all DocTypes on site as IContentType models
        /// </summary>
        /// <returns></returns>
        public IEnumerable<IContentType> GetAllDocTypes()
        {
            var list = new List<IContentType>();

            var doctypes = _services.ContentTypeService.GetAll();

            foreach (var type in doctypes)
            {
                if (type != null)
                {
                    list.Add(type);
                }
            }

            return list;
        }
        /// <summary>
        /// Gets list of all DocTypes on site as IContentType models (uses saved list, if available)
        /// </summary>
        /// <returns></returns>
        public IEnumerable<IContentTypeComposition> GetAllCompositions()
        {
            if (_AllContentTypeComps.Any())
            {
                return _AllContentTypeComps;
            }
            else
            {
                var doctypes = _services.ContentTypeService.GetAll();
                var comps = doctypes.SelectMany(n => n.ContentTypeComposition);

                _AllContentTypeComps = comps.DistinctBy(n => n.Id);

                return _AllContentTypeComps;
            }

        }

        /// <summary>
        /// Gets list of all DocTypes on site as AuditableDoctype models
        /// </summary>
        /// <returns></returns>
        public IEnumerable<AuditableDocType> GetAuditableDocTypes()
        {
            var list = new List<AuditableDocType>();

            var doctypes = _services.ContentTypeService.GetAll();

            foreach (var type in doctypes)
            {
                if (type != null)
                {
                    list.Add(ConvertIContentTypeToAuditableDocType(type));
                }
            }

            return list;
        }

        private AuditableDocType ConvertIContentTypeToAuditableDocType(IContentType ContentType)
        {
            var adt = new AuditableDocType();
            adt.ContentType = ContentType;
            adt.Name = ContentType.Name;
            adt.Alias = ContentType.Alias;
            adt.Guid = ContentType.Key;
            adt.Id = ContentType.Id;
            adt.IsElement = ContentType.IsElement;

            if (ContentType.DefaultTemplate != null)
            {
                adt.DefaultTemplateName = ContentType.DefaultTemplate.Name;
            }
            else
            {
                adt.DefaultTemplateName = "NONE";
            }

            var templates = new Dictionary<int, string>();
            foreach (var template in ContentType.AllowedTemplates)
            {
                templates.Add(template.Id, template.Alias);
            }
            adt.AllowedTemplates = templates;

            var hasComps = new Dictionary<int, string>();
            foreach (var comp in ContentType.ContentTypeComposition)
            {
                hasComps.Add(comp.Id, comp.Alias);
            }
            adt.CompositionsUsed = hasComps;

            var allCompsIds = GetAllCompositions().Select(n => n.Id);
            
            adt.IsComposition = allCompsIds.Contains(ContentType.Id);

            adt.HasContentNodes = _services.ContentTypeService.HasContentNodes(ContentType.Id);

            adt.FolderPath = GetFolderContainerPath(ContentType);

            return adt;
        }

        private List<string> GetFolderContainerPath(IContentType ContentType)
        {
            var folders = new List<string>();
            var ids = ContentType.Path.Split(',');

            try
            {
                //The final one is the DataType, so exclude it
                foreach (var sId in ids.Take(ids.Length - 1))
                {
                    if (sId != "-1")
                    {
                        var container = _services.ContentTypeService.GetContainer(Convert.ToInt32(sId));
                        if (container != null)
                        {
                            folders.Add(container.Name);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                folders.Add("~ERROR~");
                var msg = $"Error in 'GetFolderContainerPath()' for ContentType {ContentType.Id} - '{ContentType.Name}'";
                _logger.LogError(e, msg);
            }

            return folders;
        }



        ///// <summary>
        ///// Gets list of all DocTypes on site as AuditableDoctype models
        ///// </summary>
        ///// <returns></returns>
        //public static IEnumerable<AuditableDocType> GetAuditableDocTypes()
        //{
        //    var list = new List<AuditableDocType>();

        //    var doctypes = umbDocTypeService.GetAllContentTypes();

        //    foreach (var type in doctypes)
        //    {
        //        if (type != null)
        //        {
        //            list.Add(new AuditableDocType(type));
        //        }
        //    }

        //    return list;
        //}

        #endregion

        #region AuditableProperties

        public SiteAuditableProperties AllProperties()
        {
            var allProps = new SiteAuditableProperties();
            allProps.PropsForDoctype = "[All]";
            List<AuditableProperty> propertiesList = new List<AuditableProperty>();

            var allDocTypes = _services.ContentTypeService.GetAll();

            foreach (var docType in allDocTypes)
            {
                //var ct = _services.ContentTypeService.Get(docTypeAlias);

                foreach (var prop in docType.PropertyTypes)
                {
                    //test for the same property already in list
                    if (propertiesList.Exists(i => i.UmbPropertyType.Alias == prop.Alias & i.UmbPropertyType.Name == prop.Name & i.UmbPropertyType.DataTypeId == prop.DataTypeId))
                    {
                        //Add current DocType to existing property
                        var info = new PropertyDoctypeInfo();
                        info.Id = docType.Id;
                        info.DocTypeAlias = docType.Alias;
                        info.GroupName = "";
                        propertiesList.Find(i => i.UmbPropertyType.Alias == prop.Alias).AllDocTypes.Add(info);
                    }
                    else
                    {
                        //Add new property
                        AuditableProperty auditProp = PropertyTypeToAuditableProperty(prop);

                        var info = new PropertyDoctypeInfo();
                        info.DocTypeAlias = docType.Alias;
                        info.GroupName = "";

                        auditProp.AllDocTypes.Add(info);
                        propertiesList.Add(auditProp);
                    }
                }
            }

            allProps.AllProperties = propertiesList;
            return allProps;
        }

        /// <summary>
        /// Get a ContentType model for a Doctype by its alias
        /// </summary>
        /// <param name="DocTypeAlias"></param>
        /// <returns></returns>
        public IContentType GetContentTypeByAlias(string DocTypeAlias)
        {
            return _services.ContentTypeService.Get(DocTypeAlias);
        }

        public SiteAuditableProperties AllPropertiesForDocType(string DocTypeAlias)
        {
            var allProps = new SiteAuditableProperties();
            allProps.PropsForDoctype = DocTypeAlias;
            List<AuditableProperty> propertiesList = new List<AuditableProperty>();

            var ct = _services.ContentTypeService.Get(DocTypeAlias);
            var propsDone = new List<int>();

            //First, compositions
            foreach (var comp in ct.ContentTypeComposition)
            {
                foreach (var group in comp.CompositionPropertyGroups)
                {
                    foreach (var prop in group.PropertyTypes)
                    {
                        AuditableProperty auditProp = PropertyTypeToAuditableProperty(prop);

                        auditProp.InComposition = comp.Name;
                        auditProp.GroupName = group.Name;

                        propertiesList.Add(auditProp);
                        propsDone.Add(prop.Id);
                    }
                }
            }

            //Next, non-comp properties
            foreach (var group in ct.CompositionPropertyGroups)
            {
                foreach (var prop in group.PropertyTypes)
                {
                    //check if already added...
                    if (!propsDone.Contains(prop.Id))
                    {
                        AuditableProperty auditProp = PropertyTypeToAuditableProperty(prop);
                        auditProp.GroupName = group.Name;
                        auditProp.InComposition = "~NONE";
                        propertiesList.Add(auditProp);
                    }
                }

            }

            allProps.AllProperties = propertiesList;
            return allProps;
        }

        /// <summary>
        /// Meta data about a Property for Auditing purposes.
        /// </summary>
        /// <param name="UmbPropertyType"></param>
        private AuditableProperty PropertyTypeToAuditableProperty(IPropertyType UmbPropertyType)
        {
            var ap = new AuditableProperty();
            ap.UmbPropertyType = UmbPropertyType;

            ap.DataType = _services.DataTypeService.GetDataType(UmbPropertyType.DataTypeId);

            ap.DataTypeConfigType = ap.DataType.Configuration.GetType();
            try
            {
                var configDict = (Dictionary<string, string>)ap.DataType.Configuration;
                ap.DataTypeConfigDictionary = configDict;
            }
            catch (Exception e)
            {
                //ignore
                ap.DataTypeConfigDictionary = new Dictionary<string, string>();
            }

            //var  docTypes = AuditHelper.GetDocTypesForProperty(UmbPropertyType.Alias);
            // this.DocTypes = new List<string>();

            if (ap.DataType.EditorAlias.Contains("NestedContent"))
            {
                ap.IsNestedContent = true;
                var config = (NestedContentConfiguration)ap.DataType.Configuration;
                //var contentJson = ["contentTypes"];

                //var types = JsonConvert
                //    .DeserializeObject<IEnumerable<NestedContentContentTypesConfigItem>>(contentJson);
                ap.NestedContentDocTypesConfig = config.ContentTypes;
            }

            return ap;
        }

        /// <summary>
        /// Get a list of all DocTypes which contain a property of a specified Alias
        /// </summary>
        /// <param name="PropertyAlias"></param>
        /// <returns></returns>
        public List<PropertyDoctypeInfo> GetDocTypesForProperty(string PropertyAlias)
        {
            var docTypesList = new List<PropertyDoctypeInfo>();

            var allDocTypes = _services.ContentTypeService.GetAll();

            foreach (var docType in allDocTypes)
            {
                var matchingProps = docType.CompositionPropertyTypes.Where(n => n.Alias == PropertyAlias).ToList();
                if (matchingProps.Any())
                {
                    foreach (var prop in matchingProps)
                    {
                        var x = new PropertyDoctypeInfo();
                        x.DocTypeAlias = docType.Alias;

                        var matchingGroups = docType.PropertyGroups.Where(n => n.PropertyTypes.Contains(prop.Alias)).ToList();
                        if (matchingGroups.Any())
                        {
                            x.GroupName = matchingGroups.First().Name;
                        }

                        docTypesList.Add(x);
                    }
                }
            }

            return docTypesList;
        }

        private Dictionary<IPropertyType, string> PropsWithDocTypes()
        {
            var properties = new Dictionary<IPropertyType, string>();
            var docTypes = _services.ContentTypeService.GetAll();
            foreach (var doc in docTypes)
            {
                foreach (var prop in doc.PropertyTypes)
                {
                    properties.Add(prop, doc.Alias);
                }
            }

            return properties;
        }

        #endregion

        #region AuditableDataTypes

        public IEnumerable<AuditableDataType> AllDataTypes()
        {
            var list = new List<AuditableDataType>();
            var datatypes = _services.DataTypeService.GetAll();

            var properties = PropsWithDocTypes();


            foreach (var dt in datatypes)
            {
                var adt = new AuditableDataType();
                adt.Name = dt.Name;
                adt.EditorAlias = dt.EditorAlias;
                adt.Guid = dt.Key;
                adt.Id = dt.Id;
                adt.FolderPath = GetFolderContainerPath(dt);
                adt.ConfigurationJson = JsonConvert.SerializeObject(dt.Configuration);

                var matchingProps = properties.Where(p => p.Key.DataTypeId == dt.Id);
                adt.UsedOnProperties = matchingProps;

                list.Add(adt);
            }

            return list;
        }

        private List<string> GetFolderContainerPath(IDataType DataType)
        {
            var folders = new List<string>();
            var ids = DataType.Path.Split(',');

            try
            {
                //The final one is the DataType, so exclude it
                foreach (var sId in ids.Take(ids.Length - 1))
                {
                    if (sId != "-1")
                    {
                        var container = _services.DataTypeService.GetContainer(Convert.ToInt32(sId));
                        folders.Add(container.Name);
                    }
                }
            }
            catch (Exception e)
            {
                folders.Add("~ERROR~");
                var msg = $"Error in 'GetFolderContainerPath()' for DataType {DataType.Id} - '{DataType.Name}'";
                _logger.LogError(e, msg);
            }

            return folders;
        }

        #endregion

        #region Special Queries

        public IEnumerable<IGrouping<string, string>> TemplatesUsedOnContent()
        {
            var allContent = this.GetContentNodes();
            var allContentTemplates = allContent.Select(n => n.TemplateAlias);
            return allContentTemplates.GroupBy(n => n);
        }

        public IEnumerable<string> TemplatesNotUsedOnContent()
        {
            var allTemplateAliases = _services.FileService.GetTemplates().Select(n => n.Alias).ToList();

            var allContent = this.GetContentNodes();

            var contentTemplatesInUse = allContent.Select(n => n.TemplateAlias).Distinct().ToList();

            var templatesWithoutContent = allTemplateAliases.Except(contentTemplatesInUse);

            return templatesWithoutContent.OrderBy(n => n).ToList();

        }

        #endregion

        #region AuditableTemplates
        public IEnumerable<AuditableTemplate> GetAuditableTemplates()
        {
            var list = new List<AuditableTemplate>();
            var templates = _services.FileService.GetTemplates();

            var content = GetContentNodes().ToList();
            var docTypes = GetAllDocTypes().ToList();

            foreach (var temp in templates)
            {
                var at = new AuditableTemplate();
                at.Name = temp.Name;
                at.Alias = temp.Alias;
                at.Guid = temp.Key;
                at.Id = temp.Id;
                at.Udi = temp.GetUdi();
                at.FolderPath = GetFolderContainerPath(temp);
                at.IsMaster = temp.IsMasterTemplate;
                at.HasMaster = temp.MasterTemplateAlias;
                at.CodeLength = temp.Content.Length;
                at.CreateDate = temp.CreateDate;
                at.UpdateDate = temp.UpdateDate;
                at.OriginalPath = temp.OriginalPath;
                //at.XXX = temp.;
                //at.XXX = temp.UpdateDate;
                //at.ConfigurationJson = JsonConvert.SerializeObject(temp.Configuration);

                var matchingContent = content.Where(p => p.TemplateAlias == temp.Alias);
                at.UsedOnContent = matchingContent.Count();

                var doctypesAllowed = docTypes.Where(n => n.IsAllowedTemplate(temp.Id));
                if (doctypesAllowed.Any())
                {
                    at.IsAllowedOn = doctypesAllowed;
                }
                else
                {
                    at.IsAllowedOn = new List<IContentType>();
                }

                var doctypeDefault = docTypes.Where(n => n.DefaultTemplate != null && n.DefaultTemplate.Id == temp.Id);
                if (doctypeDefault.Any())
                {
                    at.DefaultTemplateFor = doctypeDefault;
                }
                else
                {
                    at.DefaultTemplateFor = new List<IContentType>();
                }

                list.Add(at);
            }

            return list;
        }

        private List<string> GetFolderContainerPath(ITemplate Template)
        {
            var folders = new List<string>();
            var ids = Template.Path.Split(',');

            try
            {
                //The final one is the current item, so exclude it
                foreach (var sId in ids.Take(ids.Length - 1))
                {
                    if (sId != "-1")
                    {
                        var container = _services.FileService.GetTemplate(Convert.ToInt32(sId));
                        folders.Add(container.Name);
                    }
                }
            }
            catch (Exception e)
            {
                folders.Add("~ERROR~");
                var msg = $"Error in 'GetFolderContainerPath()' for Template {Template.Id} - '{Template.Name}'";
                _logger.LogError(e, msg);
            }

            return folders;
        }

        private string GetTemplateAlias(IContent Content)
        {
            string templateAlias = "NONE";
            if (Content.TemplateId != null)
            {
                var template = _services.FileService.GetTemplate((int)Content.TemplateId);
                templateAlias = template.Alias;
            }

            return templateAlias;
        }
        #endregion


    }
}
