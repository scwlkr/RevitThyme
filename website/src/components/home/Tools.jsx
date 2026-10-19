import React from 'react';
import Link from '@docusaurus/Link';
import Arrow from '../Arrow';
export default function Tools() {
  return (
    <section className="tools-section page-width" id="tools" aria-labelledby="tools-title">
      <div className="section-heading">
        <div>
          <div className="eyebrow">01 / EVERYDAY TOOLS</div>
          <h2 id="tools-title">
            Less friction.
            <br />
            More model.
          </h2>
        </div>
        <p>
          Small, focused tools that work with native Revit elements. Use the ribbon, the command
          line, or your agent. The same operations power all three.
        </p>
      </div>
      <div className="tool-showcase">
        <div className="tool-copy">
          <span className="tool-number">01.1 / VIEWS</span>
          <h3>
            See the range.
            <br />
            Set it right.
          </h3>
          <p>
            Edit Top, Cut plane, Bottom and View depth together, with a section preview that makes
            their relationship clear.
          </p>
          <ul>
            <li>Feet/inches and metric entry</li>
            <li>Native validation and template guards</li>
            <li>One Revit Undo item per successful edit</li>
          </ul>
          <Link className="text-link" to="/docs/native-utilities/#view-range">
            Explore View Range <Arrow />
          </Link>
        </div>
        <figure className="product-image range-image">
          <img
            src="/generated/images/native-view-range.png"
            alt="RevitThyme View Range window with four plane settings and a section preview"
            width="1000"
            height="760"
            loading="lazy"
          />
          <figcaption>Actual RevitThyme WPF interface · View Range</figcaption>
        </figure>
      </div>
      <div className="tool-showcase tool-showcase--reverse">
        <div className="tool-copy">
          <span className="tool-number">01.2 / ANNOTATION</span>
          <h3>
            Two dimensions.
            <br />
            One less task.
          </h3>
          <p>
            Create an associative width and depth pair for a rectangular room, referenced to native
            wall finish faces.
          </p>
          <ul>
            <li>Preview before creating</li>
            <li>Room-tag clearance and duplicate checks</li>
            <li>Native dimensions you can keep editing</li>
          </ul>
          <Link className="text-link" to="/docs/native-utilities/#room-dimensions">
            Explore Room Dimensions <Arrow />
          </Link>
          <p className="feature-limit">
            Currently supports enclosed rectangles with four straight basic native walls.
          </p>
        </div>
        <figure className="product-image">
          <img
            src="/generated/images/native-room-dimensions.png"
            alt="Room Dimensions window showing room selection, inset and tag clearance settings"
            width="1000"
            height="760"
            loading="lazy"
          />
          <figcaption>Actual RevitThyme WPF interface · Room Dimensions</figcaption>
        </figure>
      </div>
      <div className="utility-note">
        <span className="eyebrow">ALSO IN THE TOOLKIT</span>
        <p>
          Inspect views and parameters. Rename with captured-target checks. Apply atomic parameter
          edits. Check for updates.
        </p>
        <Link className="text-link" to="/docs/features/">
          All features <span aria-hidden="true">→</span>
        </Link>
      </div>
    </section>
  );
}
