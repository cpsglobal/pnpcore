﻿using AngleSharp.Html.Parser;
using PnP.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PnP.Core.Model.SharePoint
{
    internal partial class Page : IPage
    {
        private bool isDefaultDescription;
        private string pageTitle;
        private readonly string pageName;
        private static readonly Expression<Func<IList, object>>[] getPagesLibraryExpression = new Expression<Func<IList, object>>[] { p => p.EnableFolderCreation,
            p => p.EnableMinorVersions, p => p.EnableModeration, p => p.EnableVersioning, p => p.RootFolder, p=>p.ListItemEntityTypeFullName };

        #region Construction

        internal Page(PnPContext context, IList pagesLibrary, IListItem pageListItem, PageLayoutType pageLayoutType = PageLayoutType.Article)
        {
            PnPContext = context;
            PagesLibrary = pagesLibrary;
            PageListItem = pageListItem;
            layoutType = pageLayoutType;
            pageHeader = new ClientSidePageHeader(PnPContext, PageHeaderType.Default, null);
        }

        #endregion

        #region Page Properties
        /// <summary>
        /// Title of the client side page
        /// </summary>
        public string PageTitle
        {
            get
            {
                return pageTitle;
            }
            set
            {
                if (!string.IsNullOrEmpty(value) && value.IndexOf('"') > 0)
                {
                    // Escape double quotes used in page title
                    pageTitle = value.Replace('"', '\"');
                }
                else
                {
                    pageTitle = value;
                }
            }
        }

        /// <summary>
        /// Collection of sections that exist on this client side page
        /// </summary>
        private readonly List<CanvasSection> sections = new List<CanvasSection>(1);
        public List<ICanvasSection> Sections
        {
            get
            {
                return sections.Cast<ICanvasSection>().ToList();
            }
        }

        public List<ICanvasControl> Controls { get; } = new List<ICanvasControl>(5);

        public List<ICanvasControl> HeaderControls { get; } = new List<ICanvasControl>();

        /// <summary>
        /// Layout type of the client side page
        /// </summary>
        private PageLayoutType layoutType;
        public PageLayoutType LayoutType
        {
            get
            {
                return layoutType;
            }
            set
            {
                layoutType = value;
            }
        }

        public string ThumbnailUrl { get; set; }

        public bool KeepDefaultWebParts { get; set; }

        /// <summary>
        /// The default section of the client side page
        /// </summary>
        public ICanvasSection DefaultSection
        {
            get
            {
                if (sections.Count > 0)
                {
                    return sections.First();
                }
                else
                {
                    if (sections.Count == 0)
                    {
                        sections.Add(new CanvasSection(this, CanvasSectionTemplate.OneColumn, 0));
                    }

                    return sections.First();
                }
            }
        }

        /// <summary>
        /// Does this page have comments disabled
        /// </summary>
        public bool CommentsDisabled
        {
            get
            {
                if (PageListItem != null)
                {
                    throw new Exception("TODO");
                    // TODO
                    //PageListItem.EnsureProperty(p => p.CommentsDisabled);
                    //return this.PageListItem.CommentsDisabled;
                }
                else
                {
                    throw new ClientException(ErrorType.PropertyNotLoaded, string.Format(PnPCoreResources.Exception_Page_ListItemNotLoaded, nameof(CommentsDisabled)));
                }
            }
        }


        /// <summary>
        /// Folder the page lives in
        /// </summary>
        public string Folder
        {
            get
            {
                if (PageListItem != null)
                {
                    return PageListItem[PageConstants.FileDirRef].ToString().Replace($"{PagesLibrary.RootFolder.ServerRelativeUrl}/", "");
                }
                else
                {
                    throw new ClientException(ErrorType.PropertyNotLoaded, string.Format(PnPCoreResources.Exception_Page_ListItemNotLoaded, nameof(Folder)));
                }
            }
        }

        /// <summary>
        /// Returns the page header for this page
        /// </summary>
        private ClientSidePageHeader pageHeader;
        public IClientSidePageHeader PageHeader
        {
            get
            {
                return pageHeader;
            }
        }

        /// <summary>
        /// ID value of the page (only available when the page was saved)
        /// </summary>
        private int? pageId;
        public int? PageId
        {
            get
            {
                return pageId;
            }
        }

        /// <summary>
        /// List the languages this page possibly can be translated into
        /// </summary>
        public List<int> SupportedUILanguages
        {
            get
            {
                throw new Exception("TODO");
            }
        }

        /// <summary>
        /// Space content field (JSON) for spaces pages
        /// </summary>
        public string SpaceContent { get; set; }

        /// <summary>
        /// Entity id field for topic pages
        /// </summary>
        public string EntityId { get; set; }

        /// <summary>
        /// Entity relations field for topic pages
        /// </summary>
        public string EntityRelations { get; set; }

        /// <summary>
        /// Entity type field for topic pages
        /// </summary>
        public string EntityType { get; set; }

        /// <summary>
        /// PnPContext to work with
        /// </summary>
        public PnPContext PnPContext { get; set; }

        /// <summary>
        /// Pages library
        /// </summary>
        public IList PagesLibrary { get; set; }

        /// <summary>
        /// ListItem linked to this page
        /// </summary>
        internal IListItem PageListItem { get; set; }

        #endregion

        #region Page Methods

        #region Load & New
        internal async static Task<List<IPage>> LoadPagesAsync(PnPContext context, string pageName)
        {
            List<IPage> loadedPages = new List<IPage>();

            // Get a reference to the pages library, reuse the existing one if the correct properties were loaded
            IList pagesLibrary = await EnsurePagesLibraryAsync(context).ConfigureAwait(false);

            // drop .aspx from the page name as page title is without extension
            if (!string.IsNullOrEmpty(pageName))
            {
                pageName = pageName.ToLowerInvariant();
                if (pageName.EndsWith(".aspx"))
                {
                    pageName = pageName.Replace(".aspx", "");
                }
            }

            // Prepare CAML query to load the list items
            await GetPagesListData(pageName, pagesLibrary).ConfigureAwait(false);

            // There can be multiple list items from previous loads, even though we just requested pages for the filter so 
            // ensure only pages mapping the requested filter are returned
            List<IListItem> pagesToLoad = null;
            if (!string.IsNullOrEmpty(pageName))
            {
                // Strip folder from the provided file name + path
                (var folderName, var pageNameWithoutFolder) = PageToPageNameAndFolder(pageName);
                var pages = pagesLibrary.Items.Where(p => p.Title.StartsWith(pageNameWithoutFolder, StringComparison.InvariantCultureIgnoreCase));
                if (pages.Any())
                {
                    pagesToLoad = pages.ToList();
                }
            }
            else
            {
                pagesToLoad = pagesLibrary.Items.ToList();
            }

            if (pagesToLoad != null)
            {
                foreach (var page in pagesToLoad)
                {
                    var loadedPage = await Page.LoadPageAsync(pagesLibrary, page).ConfigureAwait(false);
                    if (loadedPage != null)
                    {
                        loadedPages.Add(loadedPage);
                    }
                }
            }
            return loadedPages;
        }

        private static async Task GetPagesListData(string pageName, IList pagesLibrary)
        {
            (var folderName, var pageNameWithoutFolder) = PageToPageNameAndFolder(pageName);

            if (!string.IsNullOrEmpty(folderName))
            {
                folderName = $"{pagesLibrary.RootFolder.ServerRelativeUrl}/{folderName}";
            }

            string pageNameFilter = $@"
                        <BeginsWith>
                          <FieldRef Name='{PageConstants.FileLeafRef}'/>
                          <Value Type='text'>{pageNameWithoutFolder}</Value>
                        </BeginsWith>";

            // This is the main query, it can be complemented with above page name filter bij replacing the variables
            string pageQuery = $@"
                <View Scope='Recursive'>
                  <ViewFields>
                    <FieldRef Name='{PageConstants.FileType}' />
                    <FieldRef Name='{PageConstants.FileLeafRef}' />
                    <FieldRef Name='{PageConstants.FileDirRef}' />
                    <FieldRef Name='{PageConstants.IdField}' />
                    <FieldRef Name='{PageConstants.DescriptionField}' />
                    <FieldRef Name='{PageConstants.Title}' />
                    <FieldRef Name='{PageConstants.ClientSideApplicationId}' />
                    <FieldRef Name='{PageConstants.PageLayoutType}' />
                    <FieldRef Name='{PageConstants.SpaceContentField}' />
                    <FieldRef Name='{PageConstants.TopicEntityId}' />
                    <FieldRef Name='{PageConstants.TopicEntityType}' />
                    <FieldRef Name='{PageConstants.TopicEntityRelations}' />
                    <FieldRef Name='{PageConstants.CanvasField}' />
                    <FieldRef Name='{PageConstants.PageLayoutContentField}' />
                    <FieldRef Name='{PageConstants.BannerImageUrl}' />
                    <FieldRef Name='{PageConstants.PromotedStateField}' />
                  </ViewFields>
                  <Query>
                    <Where>
                      {{1}}
                        <Contains>
                          <FieldRef Name='File_x0020_Type'/>
                          <Value Type='text'>aspx</Value>
                        </Contains>
                        {{0}}
                      {{2}}
                    </Where>
                  </Query>
                </View>";

            if (!string.IsNullOrEmpty(pageNameWithoutFolder))
            {
                pageQuery = string.Format(pageQuery, pageNameFilter, "<And>", "</And>");
            }
            else
            {
                pageQuery = string.Format(pageQuery, "", "", "");
            }

            // Remove unneeded cariage returns
            pageQuery = pageQuery.Replace("\r\n", "");

            await pagesLibrary.GetListDataAsStreamAsync(new RenderListDataOptions()
            {
                ViewXml = pageQuery,
                RenderOptions = RenderListDataOptionsFlags.ListData,
                FolderServerRelativeUrl = !string.IsNullOrEmpty(folderName) ? folderName : null
            }).ConfigureAwait(false);
        }

        internal async static Task<IPage> NewPageAsync(PnPContext context, PageLayoutType pageLayoutType = PageLayoutType.Article)
        {
            // Get a reference to the pages library, reuse the existing one if the correct properties were loaded
            IList pagesLibrary = await EnsurePagesLibraryAsync(context).ConfigureAwait(false);
            return new Page(context, pagesLibrary, null, pageLayoutType);
        }

        private static async Task<IList> EnsurePagesLibraryAsync(PnPContext context)
        {
            IList pagesLibrary = null;
            if (context.Web.IsPropertyAvailable(p => p.Lists) && context.Web.Lists.Any())
            {
                foreach (var list in context.Web.Lists)
                {
                    if (list.IsPropertyAvailable(p => p.Title) && list.Title == "Site Pages")
                    {
                        if (list.ArePropertiesAvailable(getPagesLibraryExpression))
                        {
                            pagesLibrary = list;
                        }
                        break;
                    }
                }
            }

            // No pages library found, so reload it
            if (pagesLibrary == null)
            {
                pagesLibrary = await context.Web.Lists.GetByTitleAsync("Site Pages", getPagesLibraryExpression).ConfigureAwait(false);
            }

            return pagesLibrary;
        }

        public string GetTemplatesFolder()
        {
            return GetTemplatesFolderAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Returns the name of the templates folder, and creates if it doesn't exist.
        /// </summary>        
        public async Task<string> GetTemplatesFolderAsync()
        {
            IList pagesLibrary = await EnsurePagesLibraryAsync(PnPContext).ConfigureAwait(false);

            // Templates folder is indicated via a property bag entry
            await pagesLibrary.RootFolder.EnsurePropertiesAsync(p => p.Properties).ConfigureAwait(false);
            var folderGuid = pagesLibrary.RootFolder.Properties.GetString(PageConstants.TemplatesFolderGuid, null);

            if (folderGuid == null)
            {
                // No templates folder, so create one
                return await EnsureTemplatesFolderAsync(pagesLibrary).ConfigureAwait(false);
            }
            else
            {
                // Verify the templates folder exists and if not create it
                var templateFolderName = string.Empty;
                try
                {
                    var templateFolder = await PnPContext.Web.GetFolderByIdAsync(Guid.Parse(folderGuid.ToString()), p => p.Name).ConfigureAwait(false);
                    templateFolderName = templateFolder.Name;
                }
                catch (SharePointRestServiceException)
                {
                    templateFolderName = await EnsureTemplatesFolderAsync(pagesLibrary).ConfigureAwait(false);
                }
                return templateFolderName;
            }
        }

        private static async Task<string> EnsureTemplatesFolderAsync(IList pagesLibrary)
        {
            var templateFolder = await pagesLibrary.RootFolder.EnsureFolderAsync(PageConstants.DefaultTemplatesFolder).ConfigureAwait(false);
            pagesLibrary.RootFolder.Properties[PageConstants.TemplatesFolderGuid] = templateFolder.UniqueId.ToString();
            await pagesLibrary.RootFolder.Properties.UpdateAsync().ConfigureAwait(false);
            return templateFolder.Name;
        }

#pragma warning disable CA1822 // Mark members as static
        public IClientSideText NewTextPart(string text = null)
#pragma warning restore CA1822 // Mark members as static
        {
            var textPart = new ClientSideText();
            if (!string.IsNullOrEmpty(text))
            {
                textPart.Text = text;
            }
            return textPart;
        }
        public IClientSideWebPart NewWebPart(IClientSideComponent clientSideComponent = null)
        {
            ClientSideWebPart webPart;
            if (clientSideComponent != null)
            {
                webPart = new ClientSideWebPart(clientSideComponent);
            }
            else
            {
                webPart = new ClientSideWebPart();
            }

            return webPart;
        }
        #endregion

        #region Section, column and control methods
        /// <summary>
        /// Clears all control and sections from this page
        /// </summary>
        public void ClearPage()
        {
            foreach (var section in sections)
            {
                foreach (var control in section.Controls)
                {
                    (control as CanvasControl).Delete();
                }
            }

            sections.Clear();
        }

        /// <summary>
        /// Adds a new section to your client side page
        /// </summary>
        /// <param name="sectionTemplate">The <see cref="CanvasSectionTemplate"/> type of the section</param>
        /// <param name="order">Controls the order of the new section</param>
        /// <param name="zoneEmphasis">Zone emphasis (section background)</param>
        /// <param name="verticalSectionZoneEmphasis">Vertical Section Zone emphasis (section background)</param>
        public void AddSection(CanvasSectionTemplate sectionTemplate, float order, VariantThemeType zoneEmphasis, VariantThemeType verticalSectionZoneEmphasis = VariantThemeType.None)
        {
            AddSection(sectionTemplate, order, (int)zoneEmphasis, (int)verticalSectionZoneEmphasis);
        }

        /// <summary>
        /// Adds a new section to your client side page
        /// </summary>
        /// <param name="sectionTemplate">The <see cref="CanvasSectionTemplate"/> type of the section</param>
        /// <param name="order">Controls the order of the new section</param>
        /// <param name="zoneEmphasis">Zone emphasis (section background)</param>
        /// <param name="verticalSectionZoneEmphasis">Vertical Section Zone emphasis (section background)</param>
        public void AddSection(CanvasSectionTemplate sectionTemplate, float order, int zoneEmphasis, int? verticalSectionZoneEmphasis = null)
        {
            var section = new CanvasSection(this, sectionTemplate, order)
            {
                ZoneEmphasis = zoneEmphasis,
            };
            if (section.VerticalSectionColumn != null && verticalSectionZoneEmphasis.HasValue)
            {
                (section.VerticalSectionColumn as CanvasColumn).VerticalSectionEmphasis = verticalSectionZoneEmphasis;
            }
            AddSection(section);
        }

        /// <summary>
        /// Adds a new section to your client side page
        /// </summary>
        /// <param name="sectionTemplate">The <see cref="CanvasSectionTemplate"/> type of the section</param>
        /// <param name="order">Controls the order of the new section</param>
        public void AddSection(CanvasSectionTemplate sectionTemplate, float order)
        {
            var section = new CanvasSection(this, sectionTemplate, order);
            AddSection(section);
        }

        /// <summary>
        /// Adds a new section to your client side page
        /// </summary>
        /// <param name="section"><see cref="CanvasSection"/> object describing the section to add</param>
        public void AddSection(ICanvasSection section)
        {
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            if (CanThisSectionBeAdded(section as CanvasSection))
            {
                sections.Add(section as CanvasSection);
            }
        }

        /// <summary>
        /// Adds a new section to your client side page with a given order
        /// </summary>
        /// <param name="section"><see cref="CanvasSection"/> object describing the section to add</param>
        /// <param name="order">Controls the order of the new section</param>
        public void AddSection(ICanvasSection section, float order)
        {
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            if (CanThisSectionBeAdded(section as CanvasSection))
            {
                section.Order = order;
                sections.Add(section as CanvasSection);
            }
        }

        private bool CanThisSectionBeAdded(CanvasSection section)
        {
            if (Sections.Count == 0)
            {
                return true;
            }

            if (section.VerticalSectionColumn != null)
            {
                // we're adding a section that has a vertical column. This can not be combined with either another section with a vertical column or a full width section
                if (Sections.Where(p => p.VerticalSectionColumn != null).Any())
                {
                    throw new ClientException(ErrorType.Unsupported, PnPCoreResources.Exception_Page_VerticalColumnSectionExists);
                }

                if (Sections.Where(p => p.Type == CanvasSectionTemplate.OneColumnFullWidth).Any())
                {
                    throw new ClientException(ErrorType.Unsupported, PnPCoreResources.Exception_Page_VerticalColumnFullWidthSectionExists);
                }
            }

            if (section.Type == CanvasSectionTemplate.OneColumnFullWidth)
            {
                // we're adding a full width section. This can not be combined with either a vertical column section
                if (Sections.Where(p => p.VerticalSectionColumn != null).Any())
                {
                    throw new ClientException(ErrorType.Unsupported, PnPCoreResources.Exception_Page_VerticalColumnSectionExistsNoFullWidth);
                }
            }

            return true;
        }

        /// <summary>
        /// Adds a new control to your client side page using the default <see cref="ICanvasSection"/>
        /// </summary>
        /// <param name="control"><see cref="ICanvasControl"/> to add</param>
        public void AddControl(ICanvasControl control)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            // add to defaultsection and column
            if (control.Section == null)
            {
                (control as CanvasControl).section = DefaultSection;
            }
            if (control.Column == null)
            {
                (control as CanvasControl).column = DefaultSection.DefaultColumn;
            }

            Controls.Add(control);
        }

        /// <summary>
        /// Adds a new control to your client side page using the default <see cref="CanvasSection"/> using a given order
        /// </summary>
        /// <param name="control"><see cref="ICanvasControl"/> to add</param>
        /// <param name="order">Order of the control in the default section</param>
        public void AddControl(ICanvasControl control, int order)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            // add to default section and column
            if (control.Section == null)
            {
                (control as CanvasControl).section = DefaultSection;
            }
            if (control.Column == null)
            {
                (control as CanvasControl).column = DefaultSection.DefaultColumn;
            }
            control.Order = order;

            Controls.Add(control);
        }

        /// <summary>
        /// Adds a new control to your client side page in the given section
        /// </summary>
        /// <param name="control"><see cref="ICanvasControl"/> to add</param>
        /// <param name="section"><see cref="ICanvasSection"/> that will hold the control. Control will end up in the <see cref="ICanvasSection.DefaultColumn"/>.</param>
        public void AddControl(ICanvasControl control, ICanvasSection section)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            (control as CanvasControl).section = section;
            (control as CanvasControl).column = section.DefaultColumn;

            Controls.Add(control);
        }

        /// <summary>
        /// Adds a new control to your client side page in the given section with a given order
        /// </summary>
        /// <param name="control"><see cref="ICanvasControl"/> to add</param>
        /// <param name="section"><see cref="ICanvasSection"/> that will hold the control. Control will end up in the <see cref="ICanvasSection.DefaultColumn"/>.</param>
        /// <param name="order">Order of the control in the given section</param>
        public void AddControl(ICanvasControl control, ICanvasSection section, int order)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            (control as CanvasControl).section = section;
            (control as CanvasControl).column = section.DefaultColumn;
            control.Order = order;

            Controls.Add(control);
        }

        /// <summary>
        /// Adds a new control to your client side page in the given section
        /// </summary>
        /// <param name="control"><see cref="ICanvasControl"/> to add</param>
        /// <param name="column"><see cref="ICanvasColumn"/> that will hold the control</param>    
        public void AddControl(ICanvasControl control, ICanvasColumn column)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }
            if (column == null)
            {
                throw new ArgumentNullException(nameof(column));
            }

            (control as CanvasControl).section = column.Section;
            (control as CanvasControl).column = column;

            Controls.Add(control);
        }

        /// <summary>
        /// Adds a new control to your client side page in the given section with a given order
        /// </summary>
        /// <param name="control"><see cref="ICanvasControl"/> to add</param>
        /// <param name="column"><see cref="ICanvasColumn"/> that will hold the control</param>    
        /// <param name="order">Order of the control in the given section</param>
        public void AddControl(ICanvasControl control, ICanvasColumn column, int order)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }
            if (column == null)
            {
                throw new ArgumentNullException(nameof(column));
            }

            (control as CanvasControl).section = column.Section;
            (control as CanvasControl).column = column;
            control.Order = order;

            Controls.Add(control);
        }

        /// <summary>
        /// Adds a new header control to your client side page with a given order
        /// </summary>
        /// <param name="control"><see cref="ICanvasControl"/> to add</param>
        /// <param name="order">Order of the control in the given section</param>
        public void AddHeaderControl(ICanvasControl control, int order)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            (control as CanvasControl).section = DefaultSection;
            (control as CanvasControl).column = DefaultSection.DefaultColumn;
            control.Order = order;

            HeaderControls.Add(control);
        }
        #endregion

        #region Page header
        /// <summary>
        /// Removes the set page header 
        /// </summary>
        public void RemovePageHeader()
        {
            pageHeader = new ClientSidePageHeader(PnPContext, PageHeaderType.None, null);
        }

        /// <summary>
        /// Sets page header back to the default page header
        /// </summary>
        public void SetDefaultPageHeader()
        {
            pageHeader = new ClientSidePageHeader(PnPContext, PageHeaderType.Default, null);
        }

        /// <summary>
        /// Sets page header with custom focal point
        /// </summary>
        /// <param name="serverRelativeImageUrl">Server relative page header image url</param>
        /// <param name="translateX">X focal point for image</param>
        /// <param name="translateY">Y focal point for image</param>
        public void SetCustomPageHeader(string serverRelativeImageUrl, double? translateX = null, double? translateY = null)
        {
            pageHeader = new ClientSidePageHeader(PnPContext, PageHeaderType.Custom, serverRelativeImageUrl)
            {
                ImageServerRelativeUrl = serverRelativeImageUrl,
                TranslateX = translateX,
                TranslateY = translateY
            };
        }

        #endregion

        #region To Html
        internal string HeaderControlsToHtml()
        {
            if (HeaderControls.Any())
            {
                StringBuilder html = new StringBuilder();

                float order = 1;
                foreach (var headerControl in HeaderControls)
                {
                    html.Append((headerControl as CanvasControl).ToHtml(order));
                }

                return html.ToString();
            }
            else
            {
                return "";
            }
        }

        /// <summary>
        /// Returns the html representation of this client side page. This is the content that will be persisted in the <see cref="IListItem"/> list item.
        /// </summary>
        /// <returns>Html representation</returns>
        public string ToHtml()
        {
            StringBuilder html = new StringBuilder(100);

            if (sections.Count == 0) return string.Empty;

            html.Append($@"<div>");
            // Normalize section order by starting from 1, users could have started from 0 or left gaps in the numbering
            var sectionsToOrder = sections.OrderBy(p => p.Order).ToList();
            int i = 1;
            foreach (var section in sectionsToOrder)
            {
                section.Order = i;
                i++;
            }

            foreach (var section in sections.OrderBy(p => p.Order))
            {
                html.Append(section.ToHtml());

            }
            // Thumbnail
            var thumbnailData = new { controlType = 0, pageSettingsSlice = new { isDefaultDescription = isDefaultDescription, isDefaultThumbnail = string.IsNullOrEmpty(ThumbnailUrl) } };
            html.Append($@"<div data-sp-canvascontrol="""" data-sp-canvasdataversion=""1.0"" data-sp-controldata=""{JsonSerializer.Serialize(thumbnailData).Replace("\"", "&quot;")}""></div>");

            html.Append("</div>");
            return html.ToString();
        }
        #endregion

        #region From Html        

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        internal async static Task<Page> LoadPageAsync(IList pagesLibrary, IListItem item)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            if (item.Values.ContainsKey(PageConstants.ClientSideApplicationId) && item.Values[PageConstants.ClientSideApplicationId] != null && item.Values[PageConstants.ClientSideApplicationId].ToString().Equals(PageConstants.SitePagesFeatureId, StringComparison.InvariantCultureIgnoreCase))
            {
                Page loadedPage = new Page(pagesLibrary.PnPContext, pagesLibrary, item)
                {
                    pageTitle = Convert.ToString(item.Values[PageConstants.Title]),
                    pageId = item.Id
                };

                // set layout type
                if (item.Values.ContainsKey(PageConstants.PageLayoutType) && item.Values[PageConstants.PageLayoutType] != null && !string.IsNullOrEmpty(item.Values[PageConstants.PageLayoutType].ToString()))
                {
                    if (item.Values[PageConstants.PageLayoutType].ToString().Equals(PageConstants.SpacesLayoutType, StringComparison.InvariantCultureIgnoreCase))
                    {
                        loadedPage.LayoutType = PageLayoutType.Spaces;
                    }
                    else if (item.Values[PageConstants.PageLayoutType].ToString().Equals(PageConstants.TopicLayoutType, StringComparison.InvariantCultureIgnoreCase))
                    {
                        loadedPage.LayoutType = PageLayoutType.Topic;
                    }
                    else
                    {
                        loadedPage.LayoutType = (PageLayoutType)Enum.Parse(typeof(PageLayoutType), item.Values[PageConstants.PageLayoutType].ToString());
                    }
                }
                else
                {
                    throw new ClientException(ErrorType.Unsupported, PnPCoreResources.Exception_Page_LayoutUndetermined);
                }

                if (loadedPage.LayoutType == PageLayoutType.Spaces)
                {
                    if (item.Values.ContainsKey(PageConstants.SpaceContentField) && item.Values[PageConstants.SpaceContentField] != null && !string.IsNullOrEmpty(item.Values[PageConstants.SpaceContentField].ToString()))
                    {
                        loadedPage.SpaceContent = item.Values[PageConstants.SpaceContentField].ToString();
                    }
                }

                if (loadedPage.LayoutType == PageLayoutType.Topic)
                {
                    if (item.Values.ContainsKey(PageConstants.TopicEntityId) && item.Values[PageConstants.TopicEntityId] != null && !string.IsNullOrEmpty(item.Values[PageConstants.TopicEntityId].ToString()))
                    {
                        loadedPage.EntityId = item.Values[PageConstants.TopicEntityId].ToString();
                    }

                    if (item.Values.ContainsKey(PageConstants.TopicEntityRelations) && item.Values[PageConstants.TopicEntityRelations] != null && !string.IsNullOrEmpty(item.Values[PageConstants.TopicEntityRelations].ToString()))
                    {
                        loadedPage.EntityRelations = item.Values[PageConstants.TopicEntityRelations].ToString();
                    }

                    if (item.Values.ContainsKey(PageConstants.TopicEntityType) && item.Values[PageConstants.TopicEntityType] != null && !string.IsNullOrEmpty(item.Values[PageConstants.TopicEntityType].ToString()))
                    {
                        loadedPage.EntityType = item.Values[PageConstants.TopicEntityType].ToString();
                    }
                }

                // default canvas content for an empty page (this field contains the page's web part properties)
                var canvasContent1Html = @"<div><div data-sp-canvascontrol="""" data-sp-canvasdataversion=""1.0"" data-sp-controldata=""&#123;&quot;controlType&quot;&#58;0,&quot;pageSettingsSlice&quot;&#58;&#123;&quot;isDefaultDescription&quot;&#58;true,&quot;isDefaultThumbnail&quot;&#58;true&#125;&#125;""></div></div>";
                // If the canvasfield1 field is present and filled then let's parse it
                if (item.Values.ContainsKey(PageConstants.CanvasField) && !(item.Values[PageConstants.CanvasField] == null || string.IsNullOrEmpty(item.Values[PageConstants.CanvasField].ToString())))
                {
                    canvasContent1Html = item.Values[PageConstants.CanvasField].ToString();
                }
                var pageHeaderHtml = item.Values[PageConstants.PageLayoutContentField] != null ? item.Values[PageConstants.PageLayoutContentField].ToString() : "";

                loadedPage.LoadFromHtml(canvasContent1Html, pageHeaderHtml);

                return loadedPage;
            }
            else
            {
                return null;
            }
        }

        private void LoadFromHtml(string html, string pageHeaderHtml)
        {
            if (string.IsNullOrEmpty(html))
            {
                throw new ArgumentNullException(nameof(pageHeaderHtml));
            }

            HtmlParser parser = new HtmlParser(new HtmlParserOptions() { IsEmbedded = true });
            using (var document = parser.ParseDocument(html))
            {
                // select all control div's
                var clientSideControls = document.All.Where(m => m.HasAttribute(CanvasControl.ControlDataAttribute));

                // clear sections as we're constructing them from the loaded html
                sections.Clear();

                int controlOrder = 0;
                foreach (var clientSideControl in clientSideControls)
                {
                    var controlData = clientSideControl.GetAttribute(CanvasControl.ControlDataAttribute);
                    var controlType = CanvasControl.GetType(controlData);

                    if (controlType == typeof(ClientSideText))
                    {
                        var control = new ClientSideText()
                        {
                            Order = controlOrder
                        };
                        control.FromHtml(clientSideControl);

                        // Handle control positioning in sections and columns
                        ApplySectionAndColumn(control, control.SpControlData.Position, control.SpControlData.Emphasis);

                        AddControl(control);
                    }
                    else if (controlType == typeof(ClientSideWebPart))
                    {
                        var control = new ClientSideWebPart()
                        {
                            Order = controlOrder
                        };
                        control.FromHtml(clientSideControl);

                        // Handle control positioning in sections and columns
                        ApplySectionAndColumn(control, control.SpControlData.Position, control.SpControlData.Emphasis);

                        AddControl(control);
                    }
                    else if (controlType == typeof(CanvasColumn))
                    {
                        // Need to parse empty sections
                        //var jsonSerializerSettings = new JsonSerializerSettings()
                        //{
                        //    MissingMemberHandling = MissingMemberHandling.Ignore
                        //};
                        var jsonSerializerSettings = new JsonSerializerOptions() { IgnoreNullValues = true };
                        //var sectionData = JsonConvert.DeserializeObject<ClientSideCanvasData>(controlData, jsonSerializerSettings);
                        var sectionData = JsonSerializer.Deserialize<ClientSideCanvasData>(controlData, jsonSerializerSettings);

                        CanvasSection currentSection = null;
                        if (sectionData.Position != null)
                        {
                            currentSection = sections.Where(p => p.Order == sectionData.Position.ZoneIndex).FirstOrDefault();
                        }

                        if (currentSection == null)
                        {
                            if (sectionData.Position != null)
                            {
                                AddSection(new CanvasSection(this) { ZoneEmphasis = sectionData.Emphasis != null ? sectionData.Emphasis.ZoneEmphasis : 0 }, sectionData.Position.ZoneIndex);
                                currentSection = sections.Where(p => p.Order == sectionData.Position.ZoneIndex).First();
                            }
                        }

                        ICanvasColumn currentColumn = null;
                        if (sectionData.Position != null)
                        {
                            currentColumn = currentSection.Columns.Where(p => p.Order == sectionData.Position.SectionIndex).FirstOrDefault();
                        }

                        if (currentColumn == null)
                        {
                            if (sectionData.Position != null)
                            {
                                currentSection.AddColumn(new CanvasColumn(currentSection, sectionData.Position.SectionIndex, sectionData.Position.SectionFactor));
                                currentColumn = currentSection.Columns.Where(p => p.Order == sectionData.Position.SectionIndex).First();
                            }
                        }

                        if (sectionData.PageSettingsSlice != null)
                        {
                            if (sectionData.PageSettingsSlice.IsDefaultThumbnail.HasValue)
                            {
                                if (sectionData.PageSettingsSlice.IsDefaultThumbnail == false)
                                {
                                    // TODO
                                    //this.thumbnailUrl = Values[PageConstants.BannerImageUrlField] != null ? ((FieldUrlValue)this.PageListItem[BannerImageUrlField]).Url : string.Empty;
                                    ThumbnailUrl = PageListItem.Values[PageConstants.BannerImageUrlField] != null ? PageListItem.Values[PageConstants.BannerImageUrlField].ToString() : string.Empty;
                                }
                            }
                            if (sectionData.PageSettingsSlice.IsDefaultDescription.HasValue)
                            {
                                isDefaultDescription = sectionData.PageSettingsSlice.IsDefaultDescription.Value;
                            }
                        }
                    }

                    controlOrder++;
                }
            }

            // Perform vertical section column matchup if that did not happen yet
            var verticalSectionColumn = sections.Where(p => p.VerticalSectionColumn != null).FirstOrDefault();
            // Only continue if the vertical section column we found was "standalone" and not yet matched with other columns
            if (verticalSectionColumn != null && verticalSectionColumn.Columns.Count == 1)
            {
                // find another, non vertical section, column with the same zoneindex
                var matchedUpSection = sections.Where(p => p.VerticalSectionColumn == null && p.Order == verticalSectionColumn.Order).FirstOrDefault();
                if (matchedUpSection == null)
                {
                    // matchup did not yet happen, so let's handle it now
                    // Get the top section
                    var topSection = sections.Where(p => p.VerticalSectionColumn == null).OrderBy(p => p.Order).FirstOrDefault();
                    if (topSection != null)
                    {
                        // Add the "standalone" vertical section column to this section
                        topSection.MergeVerticalSectionColumn(verticalSectionColumn.Columns[0] as CanvasColumn);

                        // Move the controls to the new section/column
                        var controlsToMove = Controls.Where(p => p.Section == verticalSectionColumn);
                        if (controlsToMove.Any())
                        {
                            foreach (var controlToMove in controlsToMove)
                            {
                                (controlToMove as CanvasControl).MoveTo(topSection, topSection.VerticalSectionColumn);
                            }
                        }

                        // Remove the "standalone" vertical section column section
                        sections.Remove(verticalSectionColumn);
                    }
                }
            }

            // Perform section type detection
            foreach (var section in sections)
            {
                if (section.VerticalSectionColumn == null)
                {
                    if (section.Columns.Count == 1)
                    {
                        if (section.Columns[0].ColumnFactor == 0)
                        {
                            section.Type = CanvasSectionTemplate.OneColumnFullWidth;
                        }
                        else
                        {
                            section.Type = CanvasSectionTemplate.OneColumn;
                        }
                    }
                    else if (section.Columns.Count == 2)
                    {
                        if (section.Columns[0].ColumnFactor == 6)
                        {
                            section.Type = CanvasSectionTemplate.TwoColumn;
                        }
                        else if (section.Columns[0].ColumnFactor == 4)
                        {
                            section.Type = CanvasSectionTemplate.TwoColumnRight;
                        }
                        else if (section.Columns[0].ColumnFactor == 8)
                        {
                            section.Type = CanvasSectionTemplate.TwoColumnLeft;
                        }
                    }
                    else if (section.Columns.Count == 3)
                    {
                        section.Type = CanvasSectionTemplate.ThreeColumn;
                    }
                }
                else
                {
                    if (section.Columns.Count == 2)
                    {
                        section.Type = CanvasSectionTemplate.OneColumnVerticalSection;
                        (section.Columns[0] as CanvasColumn).ResetColumn(section.Columns[0].Order, section.Columns[0].ColumnFactor != 0 ? section.Columns[0].ColumnFactor : 12);
                    }
                    else if (section.Columns.Count == 3)
                    {
                        if (section.Columns[0].ColumnFactor == 6)
                        {
                            section.Type = CanvasSectionTemplate.TwoColumnVerticalSection;
                        }
                        else if (section.Columns[0].ColumnFactor == 4)
                        {
                            section.Type = CanvasSectionTemplate.TwoColumnRightVerticalSection;
                        }
                        else if (section.Columns[0].ColumnFactor == 8)
                        {
                            section.Type = CanvasSectionTemplate.TwoColumnLeftVerticalSection;
                        }
                    }
                    else if (section.Columns.Count == 4)
                    {
                        section.Type = CanvasSectionTemplate.ThreeColumnVerticalSection;
                    }
                }
            }

            // Reindex the control order. We're starting control order from 1 for each column.
            ReIndex();

            // Load page header controls. Cortex Topic pages do have 5 controls in the header (= controls that cannot be moved)
            if (LayoutType == PageLayoutType.Topic)
            {
                using (var document = parser.ParseDocument(pageHeaderHtml))
                {
                    // select all control div's
                    var clientSideHeaderControls = document.All.Where(m => m.HasAttribute(CanvasControl.ControlDataAttribute));

                    int headerControlOrder = 1;
                    foreach (var clientSideHeaderControl in clientSideHeaderControls)
                    {
                        // Process the extra header controls
                        var controlData = clientSideHeaderControl.GetAttribute(CanvasControl.ControlDataAttribute);

                        var control = new ClientSideWebPart()
                        {
                            Order = headerControlOrder,
                            IsHeaderControl = true,
                        };
                        control.FromHtml(clientSideHeaderControl);

                        HeaderControls.Add(control);
                        headerControlOrder++;
                    }
                }
            }
            else
            {
                // Load the page header
                pageHeader.FromHtml(pageHeaderHtml);
            }
        }

        private void ReIndex()
        {
            foreach (var section in sections.OrderBy(s => s.Order))
            {
                foreach (var column in section.Columns.OrderBy(c => c.Order))
                {
                    var indexer = 0;
                    foreach (var control in column.Controls.OrderBy(c => c.Order))
                    {
                        indexer++;
                        control.Order = indexer;
                    }
                }
            }
        }

        private void ApplySectionAndColumn(CanvasControl control, ClientSideCanvasControlPosition position, ClientSideSectionEmphasis emphasis)
        {
            if (position == null)
            {
                var currentSection = sections.FirstOrDefault();
                if (currentSection == null)
                {
                    AddSection(new CanvasSection(this) { ZoneEmphasis = 0 }, 0);
                    currentSection = sections.FirstOrDefault();
                }

                var currentColumn = currentSection.Columns.FirstOrDefault();
                if (currentColumn == null)
                {
                    currentSection.AddColumn(new CanvasColumn(currentSection));
                    currentColumn = currentSection.Columns.FirstOrDefault();
                }

                control.section = currentSection;
                control.column = currentColumn;
            }
            else
            {
                var currentSection = sections.Where(p => p.Order == position.ZoneIndex).FirstOrDefault();
                if (currentSection == null)
                {
                    AddSection(new CanvasSection(this) { ZoneEmphasis = emphasis != null ? emphasis.ZoneEmphasis : 0 }, position.ZoneIndex);
                    currentSection = sections.Where(p => p.Order == position.ZoneIndex).First();
                }

                var currentColumn = currentSection.Columns.Where(p => p.Order == position.SectionIndex).FirstOrDefault();

                // if layout index was set this means that we possibly have a vertical section column
                if (position.LayoutIndex.HasValue)
                {
                    currentColumn = currentSection.Columns.Where(p => p.Order == position.SectionIndex && p.LayoutIndex == position.LayoutIndex.Value).FirstOrDefault();
                }

                if (currentColumn == null)
                {
                    if (position.LayoutIndex.HasValue)
                    {
                        currentSection.AddColumn(new CanvasColumn(currentSection, position.SectionIndex, position.SectionFactor, position.LayoutIndex.Value));
                        currentColumn = currentSection.Columns.Where(p => p.Order == position.SectionIndex && p.LayoutIndex == position.LayoutIndex.Value).First();

                        // ZoneEmphasis on a vertical section column needs to be retained as that "overrides" the zone emphasis set on the section
                        if (currentColumn.IsVerticalSectionColumn)
                        {
                            (currentColumn as CanvasColumn).VerticalSectionEmphasis = emphasis != null ? emphasis.ZoneEmphasis : 0;
                        }
                    }
                    else
                    {
                        currentSection.AddColumn(new CanvasColumn(currentSection, position.SectionIndex, position.SectionFactor));
                        currentColumn = currentSection.Columns.Where(p => p.Order == position.SectionIndex).First();
                    }
                }

                control.section = currentSection;
                control.column = currentColumn;
            }
        }
        #endregion

        #region Create and Save page
        public void SaveAsTemplate(string pageName)
        {
            SaveAsTemplateAsync(pageName).GetAwaiter().GetResult();
        }

        public async Task SaveAsTemplateAsync(string pageName)
        {
            string pageUrl = $"{(await GetTemplatesFolderAsync().ConfigureAwait(false))}/{pageName}";

            // Save the page as template
            await SaveAsync(pageUrl).ConfigureAwait(false);
        }

        public void Save(string pageName)
        {
            SaveAsync(pageName).GetAwaiter().GetResult();
        }

        public async Task SaveAsync(string pageName)
        {
            if (string.IsNullOrEmpty(pageName))
            {
                throw new ArgumentNullException(nameof(pageName));
            }

            // Validate we're not using "wrong" layouts for the given site type
            await ValidateOneColumnFullWidthSectionUsageAsync().ConfigureAwait(false);

            // Normalize folders in page name
            if (!string.IsNullOrEmpty(pageName) && pageName.Contains("\\"))
            {
                pageName = pageName.Replace("\\", "/");
            }
            if (!string.IsNullOrEmpty(pageName) && pageName.StartsWith("/"))
            {
                pageName = pageName.Substring(1);
            }

            var pageHeaderHtml = "";
            if (pageHeader != null && pageHeader.Type != PageHeaderType.None && LayoutType != PageLayoutType.RepostPage
                && LayoutType != PageLayoutType.Topic)
            {
                // this triggers resolving of the header image which has to be done early as otherwise there will be version conflicts
                // (see here: https://github.com/SharePoint/PnP-Sites-Core/issues/2203)
                pageHeaderHtml = await pageHeader.ToHtmlAsync(PageTitle).ConfigureAwait(false);
            }

            if (LayoutType == PageLayoutType.Topic)
            {
                // If we have extra header controls (e.g. with topic pages) then we need to persist those controls to a html snippet that will need to be embedded in the header
                if (HeaderControls.Any())
                {
                    pageHeaderHtml = $"<div>{HeaderControlsToHtml()}</div>";
                }
            }

            // validate the page name
            if (!pageName.EndsWith(".aspx", StringComparison.InvariantCultureIgnoreCase))
            {
                pageName += ".aspx";
            }

            await EnsurePageListItemAsync(pageName).ConfigureAwait(false);

            string serverRelativePageName;
            bool updatingExistingPage = false;
            if (PageListItem == null)
            {
                // Page does not exist and need to be created
                serverRelativePageName = $"{PagesLibrary.RootFolder.ServerRelativeUrl}/{pageName}";

                IFolder folderHostingThePage;

                (var folderName, var pageNameWithoutFolder) = PageToPageNameAndFolder(pageName);
                if (!string.IsNullOrEmpty(folderName))
                {
                    folderHostingThePage = await PagesLibrary.RootFolder.EnsureFolderAsync($"{folderName}").ConfigureAwait(false);
                }
                else
                {
                    folderHostingThePage = PagesLibrary.RootFolder;
                }

                await folderHostingThePage.Files.AddTemplateFileAsync(serverRelativePageName, TemplateFileType.ClientSidePage).ConfigureAwait(false);

                // Get the list item data for the added page
                await EnsurePageListItemAsync(pageName).ConfigureAwait(false);

                // Hanlde the basic page configuration for a new modern page
                if (LayoutType == PageLayoutType.Spaces)
                {
                    PageListItem[PageConstants.ContentTypeId] = PageConstants.SpacesPage;
                }
                else
                {
                    PageListItem[PageConstants.ContentTypeId] = PageConstants.ModernArticlePage;
                }

                PageListItem[PageConstants.Title] = string.IsNullOrWhiteSpace(pageTitle) ? Path.GetFileNameWithoutExtension(pageName) : pageTitle;
                PageListItem[PageConstants.ClientSideApplicationId] = PageConstants.SitePagesFeatureId;

                if (LayoutType == PageLayoutType.Spaces)
                {
                    PageListItem[PageConstants.PageLayoutType] = PageConstants.SpacesLayoutType;
                    if (!string.IsNullOrEmpty(SpaceContent))
                    {
                        PageListItem[PageConstants.SpaceContentField] = SpaceContent;
                    }
                }
                else if (LayoutType == PageLayoutType.Topic)
                {
                    PageListItem[PageConstants.PageLayoutType] = PageConstants.TopicLayoutType;
                    PageListItem[PageConstants.TopicEntityId] = EntityId;
                    PageListItem[PageConstants.TopicEntityRelations] = EntityRelations;
                    PageListItem[PageConstants.TopicEntityType] = EntityType;

                    // Set the _SPSitePageFlags field
                    PageListItem[PageConstants._SPSitePageFlags] = ";#TopicPage;#";

                }
                else
                {
                    PageListItem[PageConstants.PageLayoutType] = layoutType.ToString();
                }
                if (layoutType == PageLayoutType.Article || LayoutType == PageLayoutType.Spaces)
                {
                    PageListItem[PageConstants.BannerImageUrl] = "/_layouts/15/images/sitepagethumbnail.png";
                }

                PageListItem[PageConstants.PromotedStateField] = (int)PromotedState.NotPromoted;
            }
            else
            {
                // We're updating an existing page
                updatingExistingPage = true;
                if (!string.IsNullOrWhiteSpace(pageTitle))
                {
                    PageListItem[PageConstants.Title] = pageTitle;
                }
            }

            // Persist to page field
            if (LayoutType == PageLayoutType.RepostPage)
            {
                PageListItem[PageConstants.ContentTypeId] = PageConstants.RepostPage;
                PageListItem[PageConstants.CanvasField] = "";
                PageListItem[PageConstants.PageLayoutContentField] = "";
                if (!string.IsNullOrEmpty(ThumbnailUrl))
                {
                    PageListItem[PageConstants.BannerImageUrl] = ThumbnailUrl;
                }
                if (updatingExistingPage)
                {
                    await PageListItem.UpdateAsync().ConfigureAwait(false);
                }
                else
                {
                    await PageListItem.UpdateOverwriteVersionAsync().ConfigureAwait(false);
                }

                return;
            }
            else
            {
                if (layoutType == PageLayoutType.Home && KeepDefaultWebParts)
                {
                    PageListItem[PageConstants.CanvasField] = "";
                }
                else
                {
                    PageListItem[PageConstants.CanvasField] = ToHtml();
                }

                if (!string.IsNullOrEmpty(ThumbnailUrl))
                {
                    PageListItem[PageConstants.BannerImageUrl] = ThumbnailUrl;
                }
            }

            // Persist the page header
            if (pageHeader.Type == PageHeaderType.None)
            {
                PageListItem[PageConstants.PageLayoutContentField] = ClientSidePageHeader.NoHeader(pageTitle);
                if (PageListItem.Values.ContainsKey(PageConstants._AuthorByline))
                {
                    PageListItem[PageConstants._AuthorByline] = null;
                }
                if (PageListItem.Values.ContainsKey(PageConstants._TopicHeader))
                {
                    PageListItem[PageConstants._TopicHeader] = null;
                }
            }
            else
            {
                PageListItem[PageConstants.PageLayoutContentField] = pageHeaderHtml;

                // AuthorByline depends on a field holding the author values
                if (pageHeader.AuthorByLineId > -1)
                {
                    //throw new Exception("TODO!!");
                    // TODO
                    //FieldUserValue[] userValueCollection = new FieldUserValue[1];
                    //FieldUserValue fieldUserVal = new FieldUserValue
                    //{
                    //    LookupId = this.pageHeader.AuthorByLineId
                    //};
                    //userValueCollection.SetValue(fieldUserVal, 0);
                    //PageListItem[PageConstants._AuthorByline] = userValueCollection;
                }

                // Topic header needs to be persisted in a field
                if (!string.IsNullOrEmpty(pageHeader.TopicHeader))
                {
                    PageListItem[PageConstants._TopicHeader] = PageHeader.TopicHeader;
                }
            }

            if (int.TryParse(PageListItem[PageConstants.IdField].ToString(), out int pageIdValue))
            {
                pageId = pageIdValue;
            }


            if ((layoutType == PageLayoutType.Article || LayoutType == PageLayoutType.Spaces) && PageListItem[PageConstants.BannerImageUrl] != null)
            {
                if (string.IsNullOrEmpty(PageListItem[PageConstants.BannerImageUrl].ToString()) || PageListItem[PageConstants.BannerImageUrl].ToString().IndexOf("/_layouts/15/images/sitepagethumbnail.png", StringComparison.InvariantCultureIgnoreCase) >= 0)
                {
                    string previewImageServerRelativeUrl = "";
                    if (pageHeader.Type == PageHeaderType.Custom && !string.IsNullOrEmpty(pageHeader.ImageServerRelativeUrl))
                    {
                        previewImageServerRelativeUrl = pageHeader.ImageServerRelativeUrl;
                    }
                    else
                    {
                        // iterate the web parts...if we find an unique id then let's grab that information
                        foreach (var control in this.Controls)
                        {
                            if (control is ClientSideWebPart webPart)
                            {
                                if (!string.IsNullOrEmpty(webPart.WebPartPreviewImage))
                                {
                                    previewImageServerRelativeUrl = webPart.WebPartPreviewImage;
                                    break;
                                }
                            }
                        }
                    }

                    // Validate the found preview image url
                    if (!string.IsNullOrEmpty(previewImageServerRelativeUrl) &&
                        !previewImageServerRelativeUrl.StartsWith("/_LAYOUTS", StringComparison.OrdinalIgnoreCase))
                    {
                        await PnPContext.Site.EnsurePropertiesAsync(p => p.Id).ConfigureAwait(false);
                        await PnPContext.Web.EnsurePropertiesAsync(p => p.Id).ConfigureAwait(false);

                        PageListItem[PageConstants.BannerImageUrl] = $"{PnPContext.Uri}/_layouts/15/getpreview.ashx?guidSite={PnPContext.Site.Id}&guidWeb={PnPContext.Web.Id}&guidFile={pageHeader.HeaderImageId}";
                    }
                }
            }
            

            if (LayoutType != PageLayoutType.Spaces)
            {
                if (PageListItem[PageConstants.PageLayoutType] as string != layoutType.ToString())
                {
                    PageListItem[PageConstants.PageLayoutType] = layoutType.ToString();
                }
            }

            // Try to set the page description if not yet set
            if ((layoutType == PageLayoutType.Article || LayoutType == PageLayoutType.Spaces) && PageListItem.Values.ContainsKey(PageConstants.DescriptionField))
            {
                if (PageListItem[PageConstants.DescriptionField] == null || string.IsNullOrEmpty(PageListItem[PageConstants.DescriptionField].ToString()))
                {
                    string previewText = "";
                    foreach (var control in Controls)
                    {
                        if (control is ClientSideText textPart)
                        {
                            if (!string.IsNullOrEmpty(textPart.PreviewText))
                            {
                                previewText = textPart.PreviewText;
                                break;
                            }
                        }
                    }

                    // Don't store more than 300 characters
                    PageListItem[PageConstants.DescriptionField] = previewText.Length > 300 ? previewText.Substring(0, 300) : previewText;
                }

            }

            await PageListItem.UpdateOverwriteVersionAsync().ConfigureAwait(false);
        }

        private async Task EnsurePageListItemAsync(string pageName)
        {
            (var folderName, var pageNameWithoutFolder) = PageToPageNameAndFolder(pageName);

            await GetPagesListData(pageName, PagesLibrary).ConfigureAwait(false);

            PageListItem = null;
            // Get the relevant list item
            foreach (var page in PagesLibrary.Items)
            {
                var fileDirRef = PagesLibrary.RootFolder.ServerRelativeUrl + (!string.IsNullOrEmpty(folderName) ? $"/{folderName}" : "");
                if (page[PageConstants.FileLeafRef].ToString().Equals(pageNameWithoutFolder) && page[PageConstants.FileDirRef].ToString().Equals(fileDirRef))
                {
                    PageListItem = page;
                    continue;
                }
            }
        }

        private static Tuple<string, string> PageToPageNameAndFolder(string pageName)
        {
            if (string.IsNullOrEmpty(pageName))
            {
                return new Tuple<string, string>("", "");
            }

            var folderName = "";
            var pageNameWithoutFolder = pageName;
            if (pageName.Contains("/"))
            {
                folderName = pageName.Substring(0, pageName.LastIndexOf("/"));
                pageNameWithoutFolder = pageName.Substring(pageName.LastIndexOf("/") + 1);
            }

            return new Tuple<string, string>(folderName, pageNameWithoutFolder);
        }

        private async Task ValidateOneColumnFullWidthSectionUsageAsync()
        {
            bool hasOneColumnFullWidthSection = false;
            foreach (var section in sections)
            {
                if (section.Type == CanvasSectionTemplate.OneColumnFullWidth)
                {
                    hasOneColumnFullWidthSection = true;
                    break;
                }
            }
            if (hasOneColumnFullWidthSection)
            {
                await PnPContext.Web.EnsurePropertiesAsync(p => p.WebTemplate).ConfigureAwait(true);
                if (!PnPContext.Web.WebTemplate.Equals("SITEPAGEPUBLISHING", StringComparison.InvariantCultureIgnoreCase))
                {
                    throw new ClientException(ErrorType.Unsupported, string.Format(PnPCoreResources.Exception_Page_CantUseFullWidthSection, PnPContext.Web.WebTemplate));
                }
            }
        }
        #endregion

        #region Page Translations

        // TODO

        #endregion

        #region Publishing, News promotion/demotion
        /// <summary>
        /// Publishes a client side page
        /// </summary>
        public void Publish()
        {
            PublishAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Publishes a client side page
        /// </summary>
        public async Task PublishAsync()
        {
            if (PageListItem != null)
            {
                var pageFile = await PnPContext.Web.GetFileByServerRelativeUrlAsync($"{PageListItem[PageConstants.FileDirRef]}/{PageListItem[PageConstants.FileLeafRef]}").ConfigureAwait(false);
                await pageFile.PublishAsync().ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException("You first need to save the page before you can publish");
            }
        }

        public void DemoteNewsArticle()
        {
            DemoteNewsArticleAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Demotes an client side <see cref="PageLayoutType.Article"/> news page as a regular client side page
        /// </summary>
        public async Task DemoteNewsArticleAsync()
        {
            if (LayoutType != PageLayoutType.Article)
            {
                throw new ClientException(ErrorType.Unsupported, PnPCoreResources.Exception_Page_CantPromoteHomePageAsNews);
            }

            // ensure we do have the page list item loaded
            await EnsurePageListItemAsync(pageName).ConfigureAwait(false);

            // Set promoted state
            PageListItem[PageConstants.PromotedStateField] = (int)PromotedState.NotPromoted;
            await PageListItem.UpdateOverwriteVersionAsync().ConfigureAwait(false);
        }

        public void PromoteAsNewsArticle()
        {
            PromoteAsNewsArticleAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Promotes a regular <see cref="PageLayoutType.Article"/> client side page as a news page
        /// </summary>
        public async Task PromoteAsNewsArticleAsync()
        {
            if (LayoutType == PageLayoutType.Home || layoutType == PageLayoutType.SingleWebPartAppPage)
            {
                throw new ClientException(ErrorType.Unsupported, PnPCoreResources.Exception_Page_PageCannotBePromotedAsNews);
            }

            // ensure we do have the page list item loaded
            await EnsurePageListItemAsync(pageName).ConfigureAwait(false);

            // Set promoted state
            PageListItem[PageConstants.PromotedStateField] = (int)PromotedState.Promoted;
            // Set publication date
            PageListItem[PageConstants.FirstPublishedDate] = DateTime.UtcNow;
            await PageListItem.UpdateOverwriteVersionAsync().ConfigureAwait(false);
        }

        public void PromoteAsHomePage()
        {
            PromoteAsHomePageAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Sets the current <see cref="IPage"/> as home page for the current site
        /// </summary>
        public async Task PromoteAsHomePageAsync()
        {
            if (LayoutType != PageLayoutType.Home)
            {
                throw new ClientException(ErrorType.Unsupported, PnPCoreResources.Exception_Page_CanOnlyPromoteHomePagesAsSiteHomePage);
            }

            // ensure we do have the page list item loaded
            await EnsurePageListItemAsync(pageName).ConfigureAwait(false);

            await PnPContext.Web.EnsurePropertiesAsync(p => p.RootFolder).ConfigureAwait(false);

            PnPContext.Web.RootFolder.WelcomePage = $"{PageListItem[PageConstants.FileDirRef]}/{PageListItem[PageConstants.FileLeafRef]}";
            await PnPContext.Web.RootFolder.UpdateAsync().ConfigureAwait(false);
        }
        #endregion

        #region Page Deletion
        /// <summary>
        /// Deletes a control from a page
        /// </summary>
        public async Task DeleteAsync()
        {
            if (PageListItem == null)
            {
                throw new ClientException(ErrorType.Unsupported, string.Format(PnPCoreResources.Exception_Page_PageWasNotSaved, pageName));
            }

            await PageListItem.DeleteAsync().ConfigureAwait(false);
        }

        public void Delete()
        {
            DeleteAsync().GetAwaiter().GetResult();
        }
        #endregion

        #region Get client side web parts methods
        public IEnumerable<IClientSideComponent> AvailableClientSideComponents(string name = null)
        {
            return AvailableClientSideComponentsAsync(name).GetAwaiter().GetResult();
        }

        public async Task<IEnumerable<IClientSideComponent>> AvailableClientSideComponentsAsync(string name = null)
        {
            var apiCall = new ApiCall($"_api/web/GetClientSideWebParts", ApiType.SPORest);

            var response = await (PnPContext.Web as Web).RawRequestAsync(apiCall, HttpMethod.Post).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(response.Json))
            {
                var root = JsonDocument.Parse(response.Json).RootElement.GetProperty("d").GetProperty("GetClientSideWebParts").GetProperty("results");

                var jsonSerializerSettings = new JsonSerializerOptions() { IgnoreNullValues = true };
                var clientSideComponents = JsonSerializer.Deserialize<List<ClientSideComponent>>(root.ToString(), jsonSerializerSettings);

                if (!clientSideComponents.Any())
                {
                    throw new ClientException(PnPCoreResources.Exception_Page_NoClientSideComponentsRetrieved);
                }

                if (!string.IsNullOrEmpty(name))
                {
                    return clientSideComponents.Where(p => p.Name == name);
                }

                return clientSideComponents;
            }
            else
            {
                throw new ClientException(PnPCoreResources.Exception_Page_NoClientSideComponentsRetrieved);
            }

        }
        #endregion

        #region Id <-> Enum mappings
        public DefaultWebPart WebPartIdToDefaultWebPart(string id)
        {
            return IdToDefaultWebPart(id);
        }

        internal static DefaultWebPart IdToDefaultWebPart(string id)
        {
            return (id.ToLower()) switch
            {
                "daf0b71c-6de8-4ef7-b511-faae7c388708" => DefaultWebPart.ContentRollup,
                "e377ea37-9047-43b9-8cdb-a761be2f8e09" => DefaultWebPart.BingMap,
                "490d7c76-1824-45b2-9de3-676421c997fa" => DefaultWebPart.ContentEmbed,
                "b7dd04e1-19ce-4b24-9132-b60a1c2b910d" => DefaultWebPart.DocumentEmbed,
                "d1d91016-032f-456d-98a4-721247c305e8" => DefaultWebPart.Image,
                "af8be689-990e-492a-81f7-ba3e4cd3ed9c" => DefaultWebPart.ImageGallery,
                "6410b3b6-d440-4663-8744-378976dc041e" => DefaultWebPart.LinkPreview,
                "0ef418ba-5d19-4ade-9db0-b339873291d0" => DefaultWebPart.NewsFeed,
                "a5df8fdf-b508-4b66-98a6-d83bc2597f63" => DefaultWebPart.NewsReel,
                // Seems like we've been having 2 guids to identify this web part...
                // Now that _api/web/GetClientSideWebParts returns both guids / controls we can distinguish News v NewsReel
                "8c88f208-6c77-4bdb-86a0-0c47b4316588" => DefaultWebPart.News,
                "58fcd18b-e1af-4b0a-b23b-422c2c52d5a2" => DefaultWebPart.PowerBIReportEmbed,
                "91a50c94-865f-4f5c-8b4e-e49659e69772" => DefaultWebPart.QuickChart,
                "eb95c819-ab8f-4689-bd03-0c2d65d47b1f" => DefaultWebPart.SiteActivity,
                "275c0095-a77e-4f6d-a2a0-6a7626911518" => DefaultWebPart.VideoEmbed,
                "31e9537e-f9dc-40a4-8834-0e3b7df418bc" => DefaultWebPart.YammerEmbed,
                "20745d7d-8581-4a6c-bf26-68279bc123fc" => DefaultWebPart.Events,
                "6676088b-e28e-4a90-b9cb-d0d0303cd2eb" => DefaultWebPart.GroupCalendar,
                "c4bd7b2f-7b6e-4599-8485-16504575f590" => DefaultWebPart.Hero,
                "f92bf067-bc19-489e-a556-7fe95f508720" => DefaultWebPart.List,
                "cbe7b0a9-3504-44dd-a3a3-0e5cacd07788" => DefaultWebPart.PageTitle,
                "7f718435-ee4d-431c-bdbf-9c4ff326f46e" => DefaultWebPart.People,
                "c70391ea-0b10-4ee9-b2b4-006d3fcad0cd" => DefaultWebPart.QuickLinks,
                "71c19a43-d08c-4178-8218-4df8554c0b0e" => DefaultWebPart.CustomMessageRegion,
                "2161a1c6-db61-4731-b97c-3cdb303f7cbb" => DefaultWebPart.Divider,
                "b19b3b9e-8d13-4fec-a93c-401a091c0707" => DefaultWebPart.MicrosoftForms,
                "8654b779-4886-46d4-8ffb-b5ed960ee986" => DefaultWebPart.Spacer,
                "243166f5-4dc3-4fe2-9df2-a7971b546a0a" => DefaultWebPart.ClientWebPart,
                "9d7e898c-f1bb-473a-9ace-8b415036578b" => DefaultWebPart.PowerApps,
                "7b317bca-c919-4982-af2f-8399173e5a1e" => DefaultWebPart.CodeSnippet,
                "cf91cf5d-ac23-4a7a-9dbc-cd9ea2a4e859" => DefaultWebPart.PageFields,
                "868ac3c3-cad7-4bd6-9a1c-14dc5cc8e823" => DefaultWebPart.Weather,
                "544dd15b-cf3c-441b-96da-004d5a8cea1d" => DefaultWebPart.YouTube,
                "b519c4f1-5cf7-4586-a678-2f1c62cc175a" => DefaultWebPart.MyDocuments,
                "cb3bfe97-a47f-47ca-bffb-bb9a5ff83d75" => DefaultWebPart.YammerFullFeed,
                "62cac389-787f-495d-beca-e11786162ef4" => DefaultWebPart.CountDown,
                "a8cd4347-f996-48c1-bcfb-75373fed2a27" => DefaultWebPart.ListProperties,
                "1ef5ed11-ce7b-44be-bc5e-4abd55101d16" => DefaultWebPart.MarkDown,
                "39c4c1c2-63fa-41be-8cc2-f6c0b49b253d" => DefaultWebPart.Planner,
                "7cba020c-5ccb-42e8-b6fc-75b3149aba7b" => DefaultWebPart.Sites,
                "df8e44e7-edd5-46d5-90da-aca1539313b8" => DefaultWebPart.CallToAction,
                "0f087d7f-520e-42b7-89c0-496aaf979d58" => DefaultWebPart.Button,
                _ => DefaultWebPart.ThirdParty,
            };
        }

        public string DefaultWebPartToWebPartId(DefaultWebPart webPart)
        {
            return WebPartEnumToId(webPart);
        }

        internal static string WebPartEnumToId(DefaultWebPart webPart)
        {
            return webPart switch
            {
                DefaultWebPart.ContentRollup => "daf0b71c-6de8-4ef7-b511-faae7c388708",
                DefaultWebPart.BingMap => "e377ea37-9047-43b9-8cdb-a761be2f8e09",
                DefaultWebPart.ContentEmbed => "490d7c76-1824-45b2-9de3-676421c997fa",
                DefaultWebPart.DocumentEmbed => "b7dd04e1-19ce-4b24-9132-b60a1c2b910d",
                DefaultWebPart.Image => "d1d91016-032f-456d-98a4-721247c305e8",
                DefaultWebPart.ImageGallery => "af8be689-990e-492a-81f7-ba3e4cd3ed9c",
                DefaultWebPart.LinkPreview => "6410b3b6-d440-4663-8744-378976dc041e",
                DefaultWebPart.NewsFeed => "0ef418ba-5d19-4ade-9db0-b339873291d0",
                DefaultWebPart.NewsReel => "a5df8fdf-b508-4b66-98a6-d83bc2597f63",
                DefaultWebPart.News => "8c88f208-6c77-4bdb-86a0-0c47b4316588",
                DefaultWebPart.PowerBIReportEmbed => "58fcd18b-e1af-4b0a-b23b-422c2c52d5a2",
                DefaultWebPart.QuickChart => "91a50c94-865f-4f5c-8b4e-e49659e69772",
                DefaultWebPart.SiteActivity => "eb95c819-ab8f-4689-bd03-0c2d65d47b1f",
                DefaultWebPart.VideoEmbed => "275c0095-a77e-4f6d-a2a0-6a7626911518",
                DefaultWebPart.YammerEmbed => "31e9537e-f9dc-40a4-8834-0e3b7df418bc",
                DefaultWebPart.Events => "20745d7d-8581-4a6c-bf26-68279bc123fc",
                DefaultWebPart.GroupCalendar => "6676088b-e28e-4a90-b9cb-d0d0303cd2eb",
                DefaultWebPart.Hero => "c4bd7b2f-7b6e-4599-8485-16504575f590",
                DefaultWebPart.List => "f92bf067-bc19-489e-a556-7fe95f508720",
                DefaultWebPart.PageTitle => "cbe7b0a9-3504-44dd-a3a3-0e5cacd07788",
                DefaultWebPart.People => "7f718435-ee4d-431c-bdbf-9c4ff326f46e",
                DefaultWebPart.QuickLinks => "c70391ea-0b10-4ee9-b2b4-006d3fcad0cd",
                DefaultWebPart.CustomMessageRegion => "71c19a43-d08c-4178-8218-4df8554c0b0e",
                DefaultWebPart.Divider => "2161a1c6-db61-4731-b97c-3cdb303f7cbb",
                DefaultWebPart.MicrosoftForms => "b19b3b9e-8d13-4fec-a93c-401a091c0707",
                DefaultWebPart.Spacer => "8654b779-4886-46d4-8ffb-b5ed960ee986",
                DefaultWebPart.ClientWebPart => "243166f5-4dc3-4fe2-9df2-a7971b546a0a",
                DefaultWebPart.PowerApps => "9d7e898c-f1bb-473a-9ace-8b415036578b",
                DefaultWebPart.CodeSnippet => "7b317bca-c919-4982-af2f-8399173e5a1e",
                DefaultWebPart.PageFields => "cf91cf5d-ac23-4a7a-9dbc-cd9ea2a4e859",
                DefaultWebPart.Weather => "868ac3c3-cad7-4bd6-9a1c-14dc5cc8e823",
                DefaultWebPart.YouTube => "544dd15b-cf3c-441b-96da-004d5a8cea1d",
                DefaultWebPart.MyDocuments => "b519c4f1-5cf7-4586-a678-2f1c62cc175a",
                DefaultWebPart.YammerFullFeed => "cb3bfe97-a47f-47ca-bffb-bb9a5ff83d75",
                DefaultWebPart.CountDown => "62cac389-787f-495d-beca-e11786162ef4",
                DefaultWebPart.ListProperties => "a8cd4347-f996-48c1-bcfb-75373fed2a27",
                DefaultWebPart.MarkDown => "1ef5ed11-ce7b-44be-bc5e-4abd55101d16",
                DefaultWebPart.Planner => "39c4c1c2-63fa-41be-8cc2-f6c0b49b253d",
                DefaultWebPart.Sites => "7cba020c-5ccb-42e8-b6fc-75b3149aba7b",
                DefaultWebPart.CallToAction => "df8e44e7-edd5-46d5-90da-aca1539313b8",
                DefaultWebPart.Button => "0f087d7f-520e-42b7-89c0-496aaf979d58",
                _ => "",
            };
        }
        #endregion

        #endregion
    }
}
