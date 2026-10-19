import React, { useEffect, useRef, useState } from 'react';

export default function InstallCommand() {
  const command = 'irm https://revitthyme.wlkrlabs.com/install | iex';
  const [status, setStatus] = useState('Copy');
  const timer = useRef();
  useEffect(() => () => clearTimeout(timer.current), []);
  async function copy() {
    try {
      await navigator.clipboard.writeText(command);
      setStatus('Copied');
    } catch {
      setStatus('Select to copy');
    }
    clearTimeout(timer.current);
    timer.current = setTimeout(() => setStatus('Copy'), 2500);
  }
  return (
    <div className="install-command">
      <span className="terminal-prompt" aria-hidden="true">
        PS &gt;
      </span>
      <code>{command}</code>
      <button type="button" onClick={copy} aria-label="Copy PowerShell install command">
        {status}
      </button>
      <span className="sr-only" role="status">
        {status === 'Copied' ? 'Install command copied' : ''}
      </span>
    </div>
  );
}
