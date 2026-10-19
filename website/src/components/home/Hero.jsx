import React from 'react';
import Link from '@docusaurus/Link';
import ModelDrawing from './ModelDrawing';
import product from '../../../.generated/product.json';
export default function Hero() {
  return (
    <section className="hero-section">
      <div className="hero-grid page-width">
        <div className="hero-copy">
          <div className="eyebrow">
            <span className="status-dot" /> OPEN SOURCE / REVIT 2027
          </div>
          <h1>
            A little more
            <br />
            flow. A lot more
            <br />
            <em>Revit.</em>
          </h1>
          <p className="hero-description">
            Practical native tools for everyday BIM work.
            <br className="desktop-break" /> A shared foundation for everything you build next.
          </p>
          <div className="hero-actions">
            <Link className="button button--primary button--lg" to="/download/">
              Get RevitThyme <span aria-hidden="true">↓</span>
            </Link>
            <Link className="text-link" to="/docs/">
              Read the docs <span aria-hidden="true">→</span>
            </Link>
          </div>
          <p className="hero-meta">
            Windows x64 <span>·</span> Free &amp; open source <span>·</span> {product.tag}
          </p>
        </div>
        <div className="hero-visual">
          <div className="drawing-topline">
            <span>THE REVIT TOOLKIT</span>
            <span>RTH / 027</span>
          </div>
          <ModelDrawing />
          <div className="drawing-bottom">
            <span>Built for the way you work.</span>
            <span aria-hidden="true">＋</span>
          </div>
        </div>
      </div>
    </section>
  );
}
