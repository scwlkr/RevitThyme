import React from 'react';
import Link from '@docusaurus/Link';
import Arrow from '../Arrow';
import product from '../../../.generated/product.json';
export default function Automation() {
  return (
    <section className="automation-section" aria-labelledby="automation-title">
      <div className="page-width">
        <div className="section-heading">
          <div>
            <div className="eyebrow">02 / BUILT TO CONNECT</div>
            <h2 id="automation-title">
              Your workflow.
              <br />
              Your interface.
            </h2>
          </div>
          <p>
            Native tools work without an AI account. When you want to automate, the CLI, MCP server
            and optional docked Agent use the same C# operation layer.
          </p>
        </div>
        <div className="interface-grid">
          <div className="interface-story">
            <span className="interface-label">FOR SCRIPTS &amp; AGENTS</span>
            <h3>
              Go from inspection
              <br />
              to a verified result.
            </h3>
            <p>
              {product.toolCount} MCP tools expose structured context, native operations and request
              recovery. Connect the local server to an MCP client, or use <code>rthyme</code> from
              PowerShell.
            </p>
            <div className="code-window">
              <div className="code-title">
                <span>POWERSHELL</span>
                <span aria-hidden="true">● ● ●</span>
              </div>
              <pre>
                <code>
                  <span className="code-comment"># Find a session, then inspect it explicitly</span>
                  {'\n'}rthyme sessions --json{'\n'}rthyme inspect --session C:\Test\session.json
                  --json
                </code>
              </pre>
            </div>
            <Link className="text-link" to="/docs/agent-integration/">
              Connect an agent <Arrow />
            </Link>
          </div>
          <div className="interface-story">
            <span className="interface-label">RIGHT INSIDE REVIT</span>
            <h3>
              A conversation.
              <br />
              In context.
            </h3>
            <p>
              The optional dockable Agent uses the bundled Codex runtime, your ChatGPT sign-in and
              saved conversations, pinned to the current Revit process.
            </p>
            <figure className="agent-image">
              <img
                src="/generated/images/native-agent.png"
                alt="Dockable RevitThyme Agent panel in Revit with connection and conversation controls"
                loading="lazy"
                width="880"
                height="630"
              />
            </figure>
            <Link className="text-link" to="/docs/agent/">
              Meet the built-in Agent <Arrow />
            </Link>
          </div>
        </div>
        <p className="trust-note">
          Typed edits check captured targets and expected values. Trusted C# execution is a
          separate, explicit opt-in with full process permissions.{' '}
          <Link to="/docs/security/">Read the trust model →</Link>
        </p>
      </div>
    </section>
  );
}
