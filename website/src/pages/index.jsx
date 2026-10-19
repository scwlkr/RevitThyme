import React from 'react';
import Layout from '@theme/Layout';
import Link from '@docusaurus/Link';
import InstallCommand from '../components/InstallCommand';
import product from '../../.generated/product.json';

export default function Home() {
  return (
    <Layout
      title="Native Revit tools"
      description="Open-source C# tools for Revit 2027. Ribbon, CLI and MCP."
    >
      <main className="project-home">
        <h1>Native tools for Revit.</h1>
        <p>Open-source C# toolkit. Use the ribbon, CLI, or MCP.</p>
        <p className="project-meta">
          <Link href={product.releaseUrl}>{product.tag}</Link> · Windows x64 · Revit 2027 ·
          GPL-3.0-or-later
        </p>
        <InstallCommand />
        <p className="install-note">
          Unsigned installer. Close Revit first.{' '}
          <Link to="/download/">Download or inspect the script.</Link>
        </p>
        <table className="tool-index">
          <tbody>
            <tr>
              <th scope="row">
                <Link to="/docs/native-utilities/#view-range">View Range</Link>
              </th>
              <td>Edit four plan planes with a section preview.</td>
            </tr>
            <tr>
              <th scope="row">
                <Link to="/docs/native-utilities/#room-dimensions">Room Dimensions</Link>
              </th>
              <td>Dimension rectangular rooms.</td>
            </tr>
            <tr>
              <th scope="row">
                <Link to="/docs/mcp/">CLI / MCP</Link>
              </th>
              <td>Inspect and edit through shared operations.</td>
            </tr>
            <tr>
              <th scope="row">
                <Link to="/docs/agent/">Agent</Link>
              </th>
              <td>Optional docked Codex client.</td>
            </tr>
          </tbody>
        </table>
        <img
          className="native-screenshot"
          src="/generated/images/native-view-range.png"
          alt="View Range: four native plan planes and their section preview"
          loading="lazy"
        />
      </main>
    </Layout>
  );
}
