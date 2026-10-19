import React from 'react';
export default function ModelDrawing() {
  return (
    <svg
      className="model-drawing"
      viewBox="0 0 420 360"
      role="img"
      aria-label="Architectural line drawing of a room and its view planes"
    >
      <defs>
        <pattern id="grid" width="28" height="28" patternUnits="userSpaceOnUse">
          <path d="M28 0H0V28" fill="none" stroke="currentColor" strokeWidth=".45" opacity=".19" />
        </pattern>
      </defs>
      <rect width="420" height="360" fill="url(#grid)" />
      <g fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinejoin="round">
        <path d="m75 237 142-82 139 80-142 82Z" />
        <path d="M75 237V100l142-82 139 80v137M217 18v137M75 100l139 80L356 98M214 180v137" />
        <path d="m75 177 142-82 139 80-142 82Z" strokeDasharray="5 5" opacity=".55" />
        <path d="M99 223v-94l94 54v94M257 268v-62l47-27v62" />
        <path d="m99 153 94 54m-47-26v68" opacity=".55" />
        <path
          d="m65 262 140 80m-141-87-7 13m147 67-7 13m35-8 141-82m-146 75 7 13m133-94 7 13"
          strokeWidth=".8"
        />
      </g>
      <g className="drawing-labels" fill="currentColor" fontSize="11" fontFamily="monospace">
        <text x="105" y="310" transform="rotate(30 105 310)">
          18′–0″
        </text>
        <text x="286" y="314" transform="rotate(-30 286 314)">
          14′–0″
        </text>
        <text x="17" y="42">
          01 / NATIVE GEOMETRY
        </text>
        <text x="254" y="350">
          MODEL · INSPECT · EXTEND
        </text>
      </g>
    </svg>
  );
}
