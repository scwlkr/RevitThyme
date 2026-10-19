import React from 'react';
import Layout from '@theme/Layout';
import Link from '@docusaurus/Link';
import InstallCommand from '../components/InstallCommand';
import product from '../../.generated/product.json';

const release = product.assetBase;
export default function Download() {
  return (
    <Layout
      title="Download RevitThyme"
      description="Install RevitThyme for full Revit 2027 on Windows x64. Requirements, verified downloads, checksums and recovery guidance."
    >
      <main className="download-page page-width">
        <div className="eyebrow">REVITTHYME / DOWNLOAD</div>
        <h1>
          Native tools.
          <br />
          One installation.
        </h1>
        <p className="download-lead">
          RevitThyme {product.version} includes the native add-in, <code>rthyme</code> CLI, MCP
          server and optional built-in Agent with the Codex runtime.
        </p>
        <div className="download-layout">
          <section>
            <h2>Install from PowerShell</h2>
            <p>Save your work and close Revit normally. Then run:</p>
            <InstallCommand />
            <p>
              Downloads the latest stable GitHub release and verifies its installer checksum before
              setup. No administrator permission or SDK is needed.
            </p>
            <div className="download-notice">
              <strong>Know what you’re running.</strong>
              <p>
                The installer is unsigned. Piping to <code>iex</code> trusts the script served by
                this domain and its GitHub release source. You can inspect the script or use the
                manual download below. A checksum verifies consistency with release assets; it is
                not a publisher signature.
              </p>
            </div>
            <h2>Prefer a manual download?</h2>
            <div className="hero-actions">
              <Link
                className="button button--primary"
                href={`${release}/RevitThyme-${product.version}-win-x64.exe`}
              >
                Download {product.tag} ↓
              </Link>
              <Link
                className="text-link"
                href={`${release}/RevitThyme-${product.version}-win-x64.exe.sha256`}
              >
                SHA-256 checksum ↗
              </Link>
            </div>
            <p>
              <Link href={`${release}/install.ps1`}>Inspect the version-pinned install script</Link>{' '}
              · <Link href={product.releaseUrl}>Release notes and all assets</Link>
            </p>
            <h2>Confirm it worked</h2>
            <p>
              Open a new PowerShell window and run <code>rthyme --version</code>. Launch Revit and
              find the RevitThyme tab. Use Inspect View on a disposable project to check the
              connection.
            </p>
            <p>
              <Link className="text-link" to="/docs/quick-start/">
                Follow the first-session guide →
              </Link>
            </p>
          </section>
          <aside className="requirements-panel">
            <h2>Before you install</h2>
            <dl>
              <dt>Operating system</dt>
              <dd>Windows x64</dd>
              <dt>Host application</dt>
              <dd>
                Full Autodesk Revit 2027
                <br />
                Revit LT and other years are unsupported
              </dd>
              <dt>Runtime</dt>
              <dd>
                .NET 10 Desktop and Core x64
                <br />
                Setup checks both
              </dd>
              <dt>Shell</dt>
              <dd>Windows PowerShell 5.1 or later</dd>
              <dt>AI account</dt>
              <dd>Optional; native tools need none</dd>
              <dt>Project status</dt>
              <dd>
                Pre-1.0, actively developed
                <br />
                Qualified on Revit 27.3.0.28; other builds and computers need qualification
              </dd>
            </dl>
            <hr />
            <Link to="/docs/installation/">Installation and recovery →</Link>
            <Link to="/docs/updates/">Updating and uninstalling →</Link>
            <Link to="/docs/troubleshooting/">Having trouble? →</Link>
          </aside>
        </div>
      </main>
    </Layout>
  );
}
