(() => {
  'use strict';

  const $ = (id) => document.getElementById(id);
  const state = {
    settings: null,
    topSites: [],
    bookmarks: [],
    suggestions: [],
    selected: -1,
    isIncognito: false,
    isDark: false,
    fallbackEngine: 'Bing',
    webViewVersion: ''
  };

  const icons = {
    search: '<svg class="icon" viewBox="0 0 24 24"><circle cx="11" cy="11" r="7"/><path d="m16.5 16.5 4 4"/></svg>',
    history: '<svg class="icon" viewBox="0 0 24 24"><path d="M3 12a9 9 0 1 0 3-6.7M3 4v5h5"/><path d="M12 7v5l3 2"/></svg>',
    bookmark: '<svg class="icon" viewBox="0 0 24 24"><path d="M6 4.5A1.5 1.5 0 0 1 7.5 3h9A1.5 1.5 0 0 1 18 4.5V21l-6-4-6 4V4.5Z"/></svg>',
    globe: '<svg class="icon" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c2.5 2.4 4 5.6 4 9s-1.5 6.6-4 9c-2.5-2.4-4-5.6-4-9s1.5-6.6 4-9Z"/></svg>'
  };

  const hasBridge = () => !!(window.chrome && chrome.webview && typeof chrome.webview.postMessage === 'function');

  const bridge = (() => {
    const pending = new Map();
    let seq = 0;

    function call(method, params = {}, timeoutMs = 15000) {
      if (!hasBridge()) return Promise.reject(new Error('bridge unavailable'));
      return new Promise((resolve, reject) => {
        const id = String(++seq);
        const timer = setTimeout(() => {
          pending.delete(id);
          reject(new Error('请求超时：' + method));
        }, timeoutMs);
        pending.set(id, { resolve, reject, timer });
        chrome.webview.postMessage({ id, method, params });
      });
    }

    if (hasBridge()) {
      chrome.webview.addEventListener('message', (event) => {
        const msg = event.data;
        if (!msg || typeof msg !== 'object') return;
        if (msg.type === 'response' && pending.has(msg.id)) {
          const entry = pending.get(msg.id);
          pending.delete(msg.id);
          clearTimeout(entry.timer);
          if (msg.ok) entry.resolve(msg.result);
          else entry.reject(new Error(msg.error || '操作失败'));
        } else if (msg.type === 'event') {
          handleEvent(msg);
        }
      });
    }
    return { call };
  })();

  function applyTheme(theme, isDark) {
    let dark = false;
    if (theme === 'Dark') dark = true;
    else if (theme === 'Light') dark = false;
    else dark = !!isDark;
    document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  }

  function getEngineName() {
    return (state.settings && state.settings.searchEngineName) || state.fallbackEngine;
  }

  function updatePlaceholder() {
    $('searchInput').placeholder = `在 ${getEngineName()} 中搜索，或输入网址`;
  }

  function applyBackground() {
    const bg = $('newtabBg');
    const url = state.settings && state.settings.newTabBackground;
    if (url) {
      bg.style.backgroundImage = `url("${String(url).replace(/"/g, '%22')}")`;
      document.body.classList.add('has-bg');
    } else {
      bg.style.backgroundImage = '';
      document.body.classList.remove('has-bg');
    }
    updateSettingsPanel();
  }

  function updateSettingsPanel() {
    const settings = state.settings || {};
    const recent = $('recentToggle');
    if (recent) recent.checked = !!settings.showRecentSites;

    const preview = $('newtabBgPreview');
    const img = $('newtabBgPreviewImg');
    const clear = $('clearNewtabBg');
    if (!preview || !img || !clear) return;
    const url = settings.newTabBackground;
    if (url) {
      if (img.getAttribute('src') !== url) img.src = url;
      img.hidden = false;
      preview.classList.remove('empty');
      clear.hidden = false;
    } else {
      img.removeAttribute('src');
      img.hidden = true;
      preview.classList.add('empty');
      clear.hidden = true;
    }
  }

  async function refreshSettings() {
    if (!hasBridge()) return;
    const settings = await bridge.call('getSettings');
    if (!settings) return;
    state.settings = settings;
    state.isDark = !!settings.isDark;
    applyTheme(state.settings.theme, state.isDark);
    updatePlaceholder();
    applyBackground();
    renderRecent();
    updateSettingsPanel();
  }

  function letterFor(item) {
    try {
      const host = new URL(item.url).hostname.replace(/^www\./, '');
      return host.charAt(0).toUpperCase();
    } catch (_) {
      return (item.title || '?').charAt(0).toUpperCase();
    }
  }

  function titleFor(item) {
    if (item.title && item.title.trim()) return item.title.trim();
    try { return new URL(item.url).hostname.replace(/^www\./, ''); } catch (_) { return item.url; }
  }

  function openUrl(url) {
    if (!url) return;
    bridge.call('openUrl', { url, newTab: false }).catch(showError);
  }

  function renderRecent() {
    const section = $('recent');
    const host = $('shortcuts');
    host.textContent = '';
    if (!(state.settings && state.settings.showRecentSites)) {
      section.hidden = true;
      return;
    }

    const seen = new Set();
    const items = [];
    (state.topSites || []).forEach((item) => {
      if (!item || !item.url || seen.has(item.url)) return;
      seen.add(item.url);
      items.push(item);
    });

    if (items.length === 0) {
      section.hidden = true;
      return;
    }

    items.slice(0, 8).forEach((item) => {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'shortcut';
      btn.title = item.url;
      const tile = document.createElement('span');
      tile.className = 'shortcut-tile';
      tile.textContent = letterFor(item);
      const label = document.createElement('span');
      label.className = 'shortcut-label';
      label.textContent = titleFor(item);
      btn.append(tile, label);
      btn.addEventListener('click', () => openUrl(item.url));
      host.appendChild(btn);
    });
    section.hidden = false;
  }

  function buildSuggestions(query) {
    const items = [];
    const seen = new Set();
    const add = (item) => {
      if (!item || !item.url || seen.has(item.url)) return;
      seen.add(item.url);
      items.push(item);
    };

    if (query) {
      items.push({
        kind: 'search',
        title: query,
        detail: `使用 ${getEngineName()} 搜索`,
        query
      });
    }

    const pool = [];
    (state.topSites || []).forEach((s) => pool.push({ url: s.url, title: s.title, detail: s.host, icon: 'history' }));
    (state.bookmarks || []).forEach((b) => pool.push({ url: b.url, title: b.title, detail: b.host, icon: 'bookmark' }));

    const filtered = query
      ? pool.filter((i) => (i.title || '').toLowerCase().includes(query.toLowerCase()) || (i.url || '').toLowerCase().includes(query.toLowerCase()))
      : pool;

    filtered.slice(0, 7).forEach((item) => {
      add({
        kind: 'url',
        title: titleFor(item),
        detail: item.detail || item.url,
        url: item.url,
        icon: item.url && state.bookmarks.some((b) => b.url === item.url) ? 'bookmark' : 'globe'
      });
    });
    return items.slice(0, 8);
  }

  function renderSuggestions(query) {
    const menu = $('searchMenu');
    state.suggestions = buildSuggestions(query);
    state.selected = -1;
    menu.textContent = '';
    if (state.suggestions.length === 0) {
      menu.classList.remove('open');
      return;
    }
    state.suggestions.forEach((item, index) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'search-item';
      button.dataset.index = String(index);
      const ic = document.createElement('span');
      ic.innerHTML = icons[item.kind === 'search' ? 'search' : item.icon] || icons.globe;
      const main = document.createElement('span');
      main.className = 'si-main';
      const title = document.createElement('span');
      title.className = 'si-title';
      title.textContent = item.title;
      const detail = document.createElement('span');
      detail.className = 'si-detail';
      detail.textContent = item.detail || '';
      main.append(title, detail);
      button.append(ic, main);
      if (item.kind === 'search') {
        const tag = document.createElement('span');
        tag.className = 'search-tag';
        tag.textContent = '搜索';
        button.appendChild(tag);
      }
      button.addEventListener('mousedown', (e) => {
        e.preventDefault();
        chooseSuggestion(index);
      });
      menu.appendChild(button);
    });
    menu.classList.add('open');
  }

  function updateSelection() {
    const nodes = document.querySelectorAll('.search-item');
    nodes.forEach((node, index) => node.classList.toggle('selected', index === state.selected));
    if (state.selected >= 0 && nodes[state.selected]) nodes[state.selected].scrollIntoView({ block: 'nearest' });
  }

  function chooseSuggestion(index) {
    const item = state.suggestions[index];
    if (!item) return;
    closeSuggestions();
    if (item.kind === 'search') {
      bridge.call('search', { query: item.query }).catch(showError);
    } else {
      openUrl(item.url);
    }
  }

  function closeSuggestions() {
    $('searchMenu').classList.remove('open');
    state.selected = -1;
  }

  function showError(error) {
    console.error(error);
  }

  function loadBootstrap() {
    if (!hasBridge()) {
      updatePlaceholder();
      applyBackground();
      renderRecent();
      updateSettingsPanel();
      return;
    }
    bridge.call('getBootstrap').then((data) => {
      if (!data) return;
      state.settings = data.settings || {};
      state.topSites = data.topSites || [];
      state.bookmarks = data.bookmarks || [];
      state.isIncognito = !!data.isIncognito;
      state.isDark = !!data.isDark;
      state.webViewVersion = data.webViewVersion || '';
      applyTheme(state.settings.theme, state.isDark);
      updatePlaceholder();
      applyBackground();
      renderRecent();
      updateSettingsPanel();
      document.title = '新标签页';
    }).catch(showError);
  }

  function handleEvent(msg) {
    if (msg.name === 'themeChanged') {
      state.isDark = !!msg.data;
      document.documentElement.dataset.theme = state.isDark ? 'dark' : 'light';
    } else if (msg.name === 'settingsChanged') {
      bridge.call('getSettings').then((settings) => {
        state.settings = settings || state.settings;
        state.isDark = !!settings.isDark;
        applyTheme(state.settings.theme, state.isDark);
        updatePlaceholder();
        applyBackground();
        renderRecent();
        updateSettingsPanel();
      }).catch(() => {});
    } else if (msg.name === 'bookmarksChanged' || msg.name === 'historyChanged') {
      loadBootstrap();
    }
  }

  function setupSearch() {
    const input = $('searchInput');
    const form = $('searchShell');

    form.addEventListener('submit', (e) => {
      e.preventDefault();
      const query = input.value.trim();
      if (!query) return;
      closeSuggestions();
      bridge.call('search', { query }).catch(showError);
    });

    input.addEventListener('input', () => {
      const query = input.value.trim();
      if (query) renderSuggestions(query);
      else closeSuggestions();
    });

    input.addEventListener('focus', () => {
      const query = input.value.trim();
      if (query) renderSuggestions(query);
      else closeSuggestions();
    });

    input.addEventListener('keydown', (e) => {
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        if (!state.suggestions.length) renderSuggestions(input.value.trim());
        state.selected = Math.min(state.selected + 1, state.suggestions.length - 1);
        updateSelection();
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        state.selected = Math.max(state.selected - 1, 0);
        updateSelection();
      } else if (e.key === 'Enter') {
        if (state.selected >= 0) {
          e.preventDefault();
          chooseSuggestion(state.selected);
        }
      } else if (e.key === 'Escape') {
        closeSuggestions();
        input.blur();
      }
    });

    document.addEventListener('mousedown', (e) => {
      if (!$('searchShell').contains(e.target)) closeSuggestions();
    });

    document.addEventListener('keydown', (e) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        input.focus();
        input.select();
      }
    });
  }

  function setupNewtabSettings() {
    const button = $('newtabSettingsButton');
    const panel = $('newtabSettingsPanel');
    const recent = $('recentToggle');

    button.addEventListener('click', (e) => {
      e.stopPropagation();
      panel.hidden = !panel.hidden;
    });

    $('closeNewtabSettings').addEventListener('click', () => { panel.hidden = true; });

    document.addEventListener('mousedown', (e) => {
      if (!panel.hidden && !panel.contains(e.target) && !button.contains(e.target))
        panel.hidden = true;
    });

    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') panel.hidden = true;
    });

    recent.addEventListener('change', async () => {
      const checked = recent.checked;
      try {
        const settings = await bridge.call('setSetting', { key: 'showRecentSites', value: checked });
        state.settings = settings || state.settings;
        renderRecent();
        updateSettingsPanel();
      } catch (error) {
        console.error(error);
        recent.checked = !checked;
      }
    });

    $('chooseNewtabBg').addEventListener('click', async () => {
      try {
        await bridge.call('chooseNewTabBackground', {}, 120000);
        await refreshSettings();
      } catch (error) { console.error(error); }
    });

    $('clearNewtabBg').addEventListener('click', async () => {
      try {
        await bridge.call('clearNewTabBackground', {}, 30000);
        await refreshSettings();
      } catch (error) { console.error(error); }
    });
  }

  setupSearch();
  setupNewtabSettings();
  loadBootstrap();
  setTimeout(() => {
    const input = $('searchInput');
    if (input && document.hasFocus()) input.focus();
  }, 120);
})();
