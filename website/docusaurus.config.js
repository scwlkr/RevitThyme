const { themes } = require('prism-react-renderer');

/** @type {import('@docusaurus/types').Config} */
module.exports = {
  title: 'RevitThyme',
  tagline: 'Native Revit tools. Open to more.',
  url: 'https://revitthyme.wlkrlabs.com',
  baseUrl: '/',
  trailingSlash: true,
  favicon: 'img/favicon.svg',
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
      title: 'RevitThyme',
      logo: { alt: '', src: 'img/favicon.svg', width: 30, height: 30 },
      items: [
        { to: '/docs/features/', label: 'Features', position: 'left' },
        { type: 'docSidebar', sidebarId: 'docs', label: 'Documentation', position: 'left' },
        { to: '/docs/contributing/', label: 'Contribute', position: 'left' },
        { href: 'https://github.com/scwlkr/RevitThyme', label: 'GitHub ↗', position: 'right' },
        { to: '/download/', label: 'Download', position: 'right', className: 'nav-download' },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Use RevitThyme',
          items: [
            { label: 'Install', to: '/download/' },
            { label: 'First steps', to: '/docs/quick-start/' },
            { label: 'Troubleshooting', to: '/docs/troubleshooting/' },
          ],
        },
        {
          title: 'Build with it',
          items: [
            { label: 'Contributing', to: '/docs/contributing/' },
            { label: 'MCP reference', to: '/docs/mcp/' },
            { label: 'Architecture', to: '/docs/architecture/' },
          ],
        },
        {
          title: 'Open source',
          items: [
            { label: 'Source on GitHub', href: 'https://github.com/scwlkr/RevitThyme' },
            { label: 'Changelog', to: '/docs/changelog/' },
            { label: 'Security', to: '/docs/security/' },
            {
              label: 'GPL-3.0-or-later',
              href: 'https://github.com/scwlkr/RevitThyme/blob/master/LICENSE',
            },
          ],
        },
      ],
      copyright:
        'RevitThyme · A WLKR LABS project. Independent software; not affiliated with or endorsed by Autodesk.',
    },
    prism: {
      theme: themes.github,
      darkTheme: themes.dracula,
      additionalLanguages: ['powershell', 'csharp', 'toml', 'json'],
    },
    tableOfContents: { minHeadingLevel: 2, maxHeadingLevel: 3 },
  },
};
