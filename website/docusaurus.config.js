const { themes } = require('prism-react-renderer');

/** @type {import('@docusaurus/types').Config} */
module.exports = {
  title: 'RevitThyme',
  tagline: 'Native tools for Revit',
  url: 'https://revitthyme.wlkrlabs.com',
  baseUrl: '/',
  trailingSlash: true,
  favicon: 'generated/favicon.svg',
  organizationName: 'scwlkr',
  projectName: 'RevitThyme',
  onBrokenLinks: 'throw',
  onBrokenAnchors: 'throw',
  markdown: {
    format: 'md',
    mermaid: true,
    hooks: { onBrokenMarkdownLinks: 'throw', onBrokenMarkdownImages: 'throw' },
  },
  presets: [
    [
      'classic',
      {
        docs: {
          path: '.generated/docs',
          sidebarPath: './sidebars.js',
          editUrl: ({ docPath }) => `https://github.com/scwlkr/RevitThyme/edit/master/${docPath}`,
          showLastUpdateAuthor: false,
          showLastUpdateTime: false,
        },
        blog: false,
        theme: { customCss: './src/css/custom.css' },
        sitemap: { changefreq: 'weekly', priority: 0.5, ignorePatterns: ['/search/**'] },
      },
    ],
  ],
  plugins: ['./plugins/legacy-routes.cjs'],
  themes: [
    '@docusaurus/theme-mermaid',
    [
      '@easyops-cn/docusaurus-search-local',
      {
        hashed: true,
        language: ['en'],
        indexBlog: false,
        indexPages: true,
        docsRouteBasePath: '/docs',
        docsDir: '.generated/docs',
        highlightSearchTermsOnTargetPage: true,
        searchResultLimits: 8,
      },
    ],
  ],
  themeConfig: {
    image: 'img/social-card.png',
    metadata: [{ name: 'theme-color', content: '#174445' }],
    colorMode: { defaultMode: 'light', respectPrefersColorScheme: true },
    navbar: {
      logo: {
        alt: 'RevitThyme',
        src: 'generated/branding/revitthyme-logo.svg',
        srcDark: 'generated/branding/revitthyme-logo-dark.svg',
        width: 150,
        height: 45,
      },
      items: [
        { type: 'docSidebar', sidebarId: 'docs', label: 'Docs', position: 'left' },
        { to: '/docs/contributing/', label: 'Contribute', position: 'left' },
        { href: 'https://github.com/scwlkr/RevitThyme', label: 'GitHub', position: 'left' },
      ],
    },
    footer: {
      links: [
        {
          items: [
            { label: 'Releases', to: '/docs/changelog/' },
            { label: 'Security', to: '/docs/security/' },
            {
              label: 'GPL-3.0-or-later',
              href: 'https://github.com/scwlkr/RevitThyme/blob/master/LICENSE',
            },
          ],
        },
      ],
      copyright: 'RevitThyme · WLKR LABS',
    },
    prism: {
      theme: themes.github,
      darkTheme: themes.dracula,
      additionalLanguages: ['powershell', 'csharp', 'toml', 'json'],
    },
    tableOfContents: { minHeadingLevel: 2, maxHeadingLevel: 3 },
  },
};
