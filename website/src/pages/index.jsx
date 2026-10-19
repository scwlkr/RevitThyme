import React from 'react';
import Layout from '@theme/Layout';
import Link from '@docusaurus/Link';
import InstallCommand from '../components/InstallCommand';
import Arrow from '../components/Arrow';
import Hero from '../components/home/Hero';
import Tools from '../components/home/Tools';
import Automation from '../components/home/Automation';

export default function Home() {
  return (
    <Layout
      title="Native tools for Revit 2027"
      description="Open-source Revit tools for everyday BIM work. Edit view ranges, dimension rooms and connect agents through a shared C# toolkit, CLI and MCP."
    >
      <main className="home">
        <Hero />
        <div className="technical-strip">
          <div className="page-width">
            <span>NATIVE BY DESIGN</span>
            <span>C# / .NET</span>
            <span>WPF</span>
            <span>CLI + MCP</span>
            <span>GPL-3.0-or-later</span>
          </div>
        </div>
        <Tools />
        <Automation />
        <section className="open-section page-width" aria-labelledby="open-title">
          <div>
            <div className="eyebrow">03 / OPEN TO MORE</div>
            <h2 id="open-title">
              Useful today.
              <br />
              Better together.
            </h2>
            <p>
              RevitThyme is a WLKR LABS open-source project, licensed GPL-3.0-or-later. Bring a
              small tool, a clearer guide or a reproducible bug. You can contribute to the docs and
              portable code without a Revit license.
            </p>
            <Link className="button button--primary" to="/docs/contributing/">
              Start contributing <Arrow />
            </Link>
          </div>
          <div className="contribution-list">
            <Link to="/docs/architecture/">
              <span>Understand the foundation</span>
              <span>C# · WPF · shared operations</span>
              <Arrow />
            </Link>
            <Link to="/docs/roadmap/">
              <span>See where it’s going</span>
              <span>Available, next and under consideration</span>
              <Arrow />
            </Link>
            <Link href="https://github.com/scwlkr/RevitThyme/issues">
              <span>Help shape the project</span>
              <span>Public issues and focused pull requests</span>
              <Arrow />
            </Link>
          </div>
        </section>
        <section className="install-section">
          <div className="page-width">
            <div className="eyebrow">READY WHEN YOU ARE</div>
            <h2>Make room for better work.</h2>
            <p>Full Revit 2027 · Windows x64 · Per-user installation</p>
            <InstallCommand />
            <p className="install-disclosure">
              Close Revit first, preserving unsaved work. The installer is unsigned. This command
              executes a downloaded script.{' '}
              <Link to="/download/">Review requirements, checksums and manual downloads →</Link>
            </p>
          </div>
        </section>
      </main>
    </Layout>
  );
}
