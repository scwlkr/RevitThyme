import React from 'react';

export default function SkipToContent() {
  function skip(event) {
    const target =
      document.querySelector('main') ??
      document.getElementById('__docusaurus_skipToContent_fallback');
    if (!target) return;
    event.preventDefault();
    // Keep the target focusable after activation so the next Tab starts in content.
    target.setAttribute('tabindex', '-1');
    target.focus();
    target.scrollIntoView({ block: 'start' });
  }
  return (
    <a className="skip-content" href="#__docusaurus_skipToContent_fallback" onClick={skip}>
      Skip to main content
    </a>
  );
}
