const docsToggle = document.querySelector('.docs-mobile-nav');
docsToggle?.addEventListener('click', () => {
  const open = docsToggle.getAttribute('aria-expanded') !== 'true';
  docsToggle.setAttribute('aria-expanded', String(open));
  document.querySelector('.docs-navigation').classList.toggle('is-open', open);
});
document.querySelectorAll('.copy-code').forEach(button => {
  button.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(button.parentElement.querySelector('code').textContent);
      button.textContent = 'Copied!';
    } catch {
      button.textContent = 'Select code to copy';
    }
    setTimeout(() => { button.textContent = 'Copy'; }, 2400);
  });
});

const dialog = document.querySelector('#search-dialog');
const input = document.querySelector('#search-input');
const status = document.querySelector('#search-status');
const results = document.querySelector('#search-results');
let searchIndex;
let searchOpener;
async function openSearch(opener) {
  searchOpener = opener;
  dialog.showModal();
  input.focus();
  if (!searchIndex) {
    status.textContent = 'Loading…';
    try {
      const response = await fetch('/search-index.json');
      if (!response.ok) throw new Error('Search unavailable');
      searchIndex = await response.json();
    } catch {
      status.textContent = 'Search unavailable. Use the guide navigation.';
      return;
    }
  }
  renderResults();
}
function renderResults() {
  if (!searchIndex) return;
  const query = input.value.trim().toLowerCase();
  const terms = query.split(/\s+/).filter(Boolean);
  const found = searchIndex.filter(page => terms.every(term => `${page.title} ${page.text}`.toLowerCase().includes(term))).slice(0, 9);
  results.replaceChildren();
  status.textContent = `${found.length} guide${found.length === 1 ? '' : 's'}`;
  if (!found.length) status.textContent = 'No results. Try another term.';
  found.forEach(page => {
    const link = document.createElement('a');
    link.href = page.url;
    link.className = 'search-result';
    const title = document.createElement('strong');
    title.textContent = page.title;
    const snippet = document.createElement('p');
    const position = page.text.toLowerCase().indexOf(terms[0] || '');
    const start = Math.max(0, position - 65);
    snippet.textContent = query ? `${start ? '…' : ''}${page.text.slice(start, start + 110)}…` : page.summary;
    link.append(title, snippet);
    results.append(link);
  });
}
document.querySelectorAll('[data-open-search]').forEach(button => button.addEventListener('click', () => openSearch(button)));
document.querySelector('[data-close-search]').addEventListener('click', () => dialog.close());
dialog.addEventListener('click', event => { if (event.target === dialog) { const bounds = dialog.getBoundingClientRect(); if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close(); } });
dialog.addEventListener('close', () => searchOpener?.focus());
input.addEventListener('input', renderResults);
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && dialog.open) {
    event.preventDefault();
    dialog.close();
    return;
  }
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
    event.preventDefault();
    if (!dialog.open) openSearch(document.activeElement);
  }
});
