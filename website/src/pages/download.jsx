import React from 'react';
import Layout from '@theme/Layout';
import Link from '@docusaurus/Link';
import InstallCommand from '../components/InstallCommand';
import product from '../../.generated/product.json';

export default function Download() {
  return (
    <Layout
      title="Download"
      description="RevitThyme installer for Windows x64 and full Revit 2027."
    >
      <main className="project-home download-page">
        <h1>Download RevitThyme {product.version}</h1>
        <p>Windows x64 · Full Revit 2027 · .NET 10</p>
        <p>Save your work and close Revit, then run:</p>
        <InstallCommand />
        <p className="install-note">
          This executes a downloaded script. The installer is unsigned; the bootstrap verifies its
          checksum, not a publisher signature.
        </p>
        <ul className="download-links">
          <li>
            <Link href={`${product.assetBase}/RevitThyme-${product.version}-win-x64.exe`}>
              Windows installer
            </Link>{' '}
            ·{' '}
            <Link href={`${product.assetBase}/RevitThyme-${product.version}-win-x64.exe.sha256`}>
              SHA-256
            </Link>
          </li>
          <li>
            <Link href={`${product.assetBase}/install.ps1`}>Install script</Link> ·{' '}
            <Link href={product.releaseUrl}>Release notes</Link>
          </li>
          <li>
            <Link href={`${product.assetBase}/TimberFold-physical-test.zip`}>
              Small TimberFold test pack
            </Link>{' '}
            · calibration cuts, six-panel gable kit and editable Revit example
          </li>
        </ul>
        <p>
          Includes the add-in, CLI, MCP server and optional Codex Agent. No SDK or administrator
          access required.
        </p>
        <p>
          Pre-1.0. Revit LT and other years are unsupported. Native qualification: Revit 27.3.0.28
          on the recorded test host.
        </p>
        <p>
          Physical fabrication remains outstanding. Measure stock and kerf, cut calibration pieces
          first, then check joints, window bars and assembly before accepting a physical kit.
        </p>
        <p>
          <Link to="/docs/quick-start/">First steps</Link> ·{' '}
          <Link to="/docs/installation/">Installation and recovery</Link> ·{' '}
          <Link to="/docs/troubleshooting/">Troubleshooting</Link>
        </p>
      </main>
    </Layout>
  );
}
