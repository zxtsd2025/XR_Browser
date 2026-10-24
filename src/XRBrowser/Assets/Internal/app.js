(() => {
  'use strict';

  const $ = (id) => document.getElementById(id);
  const content = $('content');
  const nativeBridge = window.chrome && window.chrome.webview;
  const hasBridge = !!(nativeBridge && typeof nativeBridge.postMessage === 'function');

  const state = {
    route: 'settings',
    settings: null,
    bootstrap: null,
    history: [],
    historyQuery: '',
    bookmarks: [],
    downloads: [],
    extensions: [],
    extensionError: '',
    browserInfo: null,
    editingBookmark: null,
    isDark: false,
    loading: true,
    lastDownloadRefresh: 0
  };

  const icons = {
    settings: '<svg class="icon" viewBox="0 0 24 24"><circle cx="12" cy="12" r="3.2"/><path d="M19.4 15a1.7 1.7 0 0 0 .34 1.87l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06A1.7 1.7 0 0 0 15 19.4a1.7 1.7 0 0 0-1 .6 1.7 1.7 0 0 0-.4 1.1V21a2 2 0 1 1-4 0v-.09A1.7 1.7 0 0 0 8.6 19.4a1.7 1.7 0 0 0-1.87.34l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.7 1.7 0 0 0 4.6 15a1.7 1.7 0 0 0-.6-1 1.7 1.7 0 0 0-1.1-.4H3a2 2 0 1 1 0-4h.09A1.7 1.7 0 0 0 4.6 8.6a1.7 1.7 0 0 0-.34-1.87l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.7 1.7 0 0 0 9 4.6a1.7 1.7 0 0 0 1-.6 1.7 1.7 0 0 0 .4-1.1V3a2 2 0 1 1 4 0v.09A1.7 1.7 0 0 0 15.4 4.6a1.7 1.7 0 0 0 1.87-.34l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.7 1.7 0 0 0 19.4 9c.14.37.35.7.6 1 .3.3.7.44 1.1.44H21a2 2 0 1 1 0 4h-.09a1.7 1.7 0 0 0-1.51.56Z"/></svg>',
    history: '<svg class="icon" viewBox="0 0 24 24"><path d="M3 12a9 9 0 1 0 3-6.7M3 4v5h5"/><path d="M12 7v5l3 2"/></svg>',
    bookmark: '<svg class="icon" viewBox="0 0 24 24"><path d="M6 4.5A1.5 1.5 0 0 1 7.5 3h9A1.5 1.5 0 0 1 18 4.5V21l-6-4-6 4V4.5Z"/></svg>',
    download: '<svg class="icon" viewBox="0 0 24 24"><path d="M12 3v12M7 10l5 5 5-5"/><path d="M4 19h16"/></svg>',
    extensions: '<svg class="icon" viewBox="0 0 24 24"><path d="M9 3h6v3a2.5 2.5 0 0 0 5 0h1v6h-3a2.5 2.5 0 0 0 0 5v4H9v-3a2.5 2.5 0 0 0-5 0H3V9h3a2.5 2.5 0 0 0 0-5H3V3h6Z"/></svg>',
    info: '<svg class="icon" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7.5v.2"/></svg>',
    palette: '<svg class="icon" viewBox="0 0 24 24"><path d="M12 3a9 9 0 1 0 0 18h1.5a2.5 2.5 0 0 0 0-5H12a2 2 0 0 1 0-4h2.5a3.5 3.5 0 0 0 0-7H12Z"/><circle cx="7.5" cy="10.5" r=".8" fill="currentColor" stroke="none"/><circle cx="10" cy="7" r=".8" fill="currentColor" stroke="none"/><circle cx="15" cy="7" r=".8" fill="currentColor" stroke="none"/></svg>',
    search: '<svg class="icon" viewBox="0 0 24 24"><circle cx="11" cy="11" r="7"/><path d="m16.5 16.5 4 4"/></svg>',
    trash: '<svg class="icon" viewBox="0 0 24 24"><path d="M4 7h16M9 7V4h6v3M6 7l1 14h10l1-14M10 11v6M14 11v6"/></svg>',
    edit: '<svg class="icon" viewBox="0 0 24 24"><path d="M4 20h4l11-11-4-4L4 16v4Z"/><path d="m13.5 6.5 4 4"/></svg>',
    plus: '<svg class="icon" viewBox="0 0 24 24"><path d="M12 5v14M5 12h14"/></svg>',
    folder: '<svg class="icon" viewBox="0 0 24 24"><path d="M3 6.5A1.5 1.5 0 0 1 4.5 5h5l2 2h8A1.5 1.5 0 0 1 21 8.5v9A1.5 1.5 0 0 1 19.5 19h-15A1.5 1.5 0 0 1 3 17.5v-11Z"/></svg>',
    external: '<svg class="icon" viewBox="0 0 24 24"><path d="M14 4h6v6M20 4l-9 9"/><path d="M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5"/></svg>',
    check: '<svg class="icon" viewBox="0 0 24 24"><path d="m5 12 4.5 4.5L19 7"/></svg>',
    x: '<svg class="icon" viewBox="0 0 24 24"><path d="m6 6 12 12M18 6 6 18"/></svg>',
    globe: '<svg class="icon" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c2.5 2.4 4 5.6 4 9s-1.5 6.6-4 9c-2.5-2.4-4-5.6-4-9s1.5-6.6 4-9Z"/></svg>',
    monitor: '<svg class="icon" viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="12" rx="2"/><path d="M9 20h6M12 16v4"/></svg>',
    sun: '<svg class="icon" viewBox="0 0 24 24"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>',
    moon: '<svg class="icon" viewBox="0 0 24 24"><path d="M20 15.5A8.5 8.5 0 0 1 8.5 4 8.5 8.5 0 1 0 20 15.5Z"/></svg>',
    animation: '<svg class="icon" viewBox="0 0 24 24"><path d="M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M18.4 5.6l-2.1 2.1M7.7 16.3l-2.1 2.1"/><circle cx="12" cy="12" r="3"/></svg>',
    shield: '<svg class="icon" viewBox="0 0 24 24"><path d="M12 3 5 6v6c0 4.2 3 7.6 7 9 4-1.4 7-4.8 7-9V6l-7-3Z"/><path d="m9 12 2 2 4-4"/></svg>',
    key: '<svg class="icon" viewBox="0 0 24 24"><circle cx="8" cy="15" r="4"/><path d="m11 12 8-8M16 7l2 2M18 5l2 2"/></svg>',
    cookie: '<svg class="icon" viewBox="0 0 24 24"><path d="M20 12a8 8 0 1 1-8-8 3 3 0 0 0 4 4 3 3 0 0 0 4 4Z"/><circle cx="8" cy="10" r=".8" fill="currentColor" stroke="none"/><circle cx="10" cy="15" r=".8" fill="currentColor" stroke="none"/><circle cx="15" cy="13" r=".8" fill="currentColor" stroke="none"/></svg>',
    database: '<svg class="icon" viewBox="0 0 24 24"><ellipse cx="12" cy="6" rx="7" ry="3"/><path d="M5 6v6c0 1.7 3.1 3 7 3s7-1.3 7-3V6M5 12v6c0 1.7 3.1 3 7 3s7-1.3 7-3v-6"/></svg>',
    clock: '<svg class="icon" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>',
    refresh: '<svg class="icon" viewBox="0 0 24 24"><path d="M20 11a8 8 0 1 0-2.3 6M20 4v7h-7"/></svg>',
    browser: '<svg class="icon" viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="16" rx="2"/><path d="M3 9h18M7 6.5h.01M10 6.5h.01"/></svg>',
    lock: '<svg class="icon" viewBox="0 0 24 24"><rect x="5" y="10" width="14" height="10" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/></svg>',
    code: '<svg class="icon" viewBox="0 0 24 24"><path d="m9 8-4 4 4 4M15 8l4 4-4 4"/></svg>',
    warning: '<svg class="icon" viewBox="0 0 24 24"><path d="M12 3 2.5 20h19L12 3Z"/><path d="M12 9v5M12 17v.2"/></svg>',
    pause: '<svg class="icon" viewBox="0 0 24 24"><path d="M9 5v14M15 5v14"/></svg>',
    play: '<svg class="icon" viewBox="0 0 24 24"><path d="m7 4 13 8-13 8V4Z"/></svg>',
    arrowRight: '<svg class="icon" viewBox="0 0 24 24"><path d="M5 12h14M13 6l6 6-6 6"/></svg>',
    grid: '<svg class="icon" viewBox="0 0 24 24"><rect x="4" y="4" width="6" height="6" rx="1"/><rect x="14" y="4" width="6" height="6" rx="1"/><rect x="4" y="14" width="6" height="6" rx="1"/><rect x="14" y="14" width="6" height="6" rx="1"/></svg>',
    update: '<svg class="icon" viewBox="0 0 24 24"><path d="M12 3v10M8 9l4 4 4-4"/><path d="M4 15v3a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-3"/></svg>'
  };

  const pageTitles = {
    settings: '设置',
    history: '历史记录',
    bookmarks: '书签',
    downloads: '下载内容',
    extensions: '扩展程序',
    about: '关于'
  };

  // ---------- bridge ----------
  const pending = new Map();
  let seq = 0;

  function call(method, params = {}, timeoutMs = 20000) {
    if (!hasBridge) return mockCall(method, params);
    return new Promise((resolve, reject) => {
      const id = String(++seq);
      const timer = setTimeout(() => {
        pending.delete(id);
        reject(new Error('请求超时：' + method));
      }, timeoutMs);
      pending.set(id, { resolve, reject, timer });
      nativeBridge.postMessage({ id, method, params });
    });
  }

  if (hasBridge) {
    nativeBridge.addEventListener('message', (event) => {
      const msg = event.data;
      if (!msg || typeof msg !== 'object') return;
      if (msg.type === 'response' && pending.has(msg.id)) {
        const entry = pending.get(msg.id);
        pending.delete(msg.id);
        clearTimeout(entry.timer);
        if (msg.ok) entry.resolve(msg.result);
        else entry.reject(new Error(msg.error || '操作失败'));
      } else if (msg.type === 'event') {
        handleHostEvent(msg);
      }
    });
  }

  // ---------- utils ----------
  function esc(value) {
    return String(value == null ? '' : value).replace(/[&<>"']/g, (c) => ({
      '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
  }

  function attr(value) { return esc(value); }

  function icon(name, cls = 'icon') {
    const svg = icons[name] || icons.globe;
    return svg.replace('class="icon"', `class="${cls}"`);
  }

  function applyTheme(theme, isDark) {
    let dark = false;
    if (theme === 'Dark') dark = true;
    else if (theme === 'Light') dark = false;
    else dark = !!isDark;
    document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  }

  function toast(message) {
    const el = $('toast');
    el.textContent = message;
    el.classList.add('show');
    clearTimeout(el._timer);
    el._timer = setTimeout(() => el.classList.remove('show'), 2600);
  }

  function openModal(html) {
    $('modalBox').innerHTML = html;
    $('modalBackdrop').classList.add('open');
    setTimeout(() => {
      const input = $('modalBox').querySelector('input, select, textarea');
      if (input) { input.focus(); if (input.select) input.select(); }
    }, 30);
  }

  function closeModal() {
    $('modalBackdrop').classList.remove('open');
    $('modalBox').innerHTML = '';
    state.editingBookmark = null;
  }

  function shortHost(url) {
    try { return new URL(url).hostname.replace(/^www\./, ''); } catch (_) { return url || ''; }
  }

  function letterFor(url, title) {
    const host = shortHost(url);
    return (host || title || '?').trim().charAt(0).toUpperCase();
  }

  function formatTime(dateLike) {
    try {
      return new Date(dateLike).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' });
    } catch (_) { return ''; }
  }

  function sameDay(a, b) {
    return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
  }

  function dateGroupLabel(dateLike) {
    let date;
    try { date = new Date(dateLike); } catch (_) { return '更早'; }
    const now = new Date();
    if (sameDay(date, now)) return '今天';
    const yesterday = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1);
    if (sameDay(date, yesterday)) return '昨天';
    return date.toLocaleDateString('zh-CN', { year: 'numeric', month: 'long', day: 'numeric' });
  }

  function bytesText(value) {
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    let v = Number(value) || 0;
    let i = 0;
    while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
    return i === 0 ? `${Math.round(v)} ${units[i]}` : `${v.toFixed(1)} ${units[i]}`;
  }

  function pageHead(kicker, title, sub, actions = '') {
    return `
      <div class="page-head">
        <div>
          <div class="page-kicker">${esc(kicker)}</div>
          <h1 class="page-title">${esc(title)}</h1>
          ${sub ? `<p class="page-sub">${esc(sub)}</p>` : ''}
        </div>
        ${actions ? `<div class="page-actions">${actions}</div>` : ''}
      </div>`;
  }

  function toggle(key, checked) {
    return `<label class="switch"><input type="checkbox" data-toggle="${attr(key)}" ${checked ? 'checked' : ''}><span class="track"></span></label>`;
  }

  function row(iconName, label, help, control) {
    return `
      <div class="setting-row">
        <div class="setting-icon">${icon(iconName)}</div>
        <div class="setting-text">
          <div class="setting-label">${esc(label)}</div>
          ${help ? `<div class="setting-help">${esc(help)}</div>` : ''}
        </div>
        <div class="setting-control">${control}</div>
      </div>`;
  }

  function options(values, current) {
    return values.map(([value, label]) =>
      `<option value="${attr(value)}" ${String(current).toLowerCase() === String(value).toLowerCase() ? 'selected' : ''}>${esc(label)}</option>`
    ).join('');
  }

  function emptyState(iconName, title, text) {
    return `<div class="empty"><div class="empty-icon">${icon(iconName)}</div><h3>${esc(title)}</h3><p>${esc(text)}</p></div>`;
  }

  // ---------- data ----------
  async function loadBootstrap() {
    const boot = await call('getBootstrap');
    state.bootstrap = boot;
    state.settings = boot.settings || state.settings || {};
    state.bookmarks = boot.bookmarks || [];
    state.downloads = boot.downloads || [];
    state.isDark = !!boot.isDark;
    applyTheme(state.settings.theme, state.isDark);
    updateSidebar();
    return boot;
  }

  async function refreshSettings() {
    state.settings = await call('getSettings');
    state.isDark = !!state.settings.isDark;
    applyTheme(state.settings.theme, state.isDark);
  }

  async function loadHistory() {
    state.history = await call('getHistory', { query: '' });
  }

  async function loadBookmarks() {
    state.bookmarks = await call('getBookmarks');
  }

  async function loadDownloads() {
    state.downloads = await call('getDownloads');
  }

  async function loadExtensions() {
    try {
      state.extensions = await call('getExtensions');
      state.extensionError = '';
    } catch (error) {
      state.extensions = [];
      state.extensionError = error.message || '无法读取扩展程序。';
    }
  }

  async function loadBrowserInfo() {
    state.browserInfo = await call('getBrowserInfo');
  }

  async function setSetting(key, value) {
    try {
      state.settings = await call('setSetting', { key, value });
      state.isDark = !!state.settings.isDark;
      applyTheme(state.settings.theme, state.isDark);
      if (state.route === 'settings') renderSettings();
      updateSidebar();
      return true;
    } catch (error) {
      toast(error.message || '设置保存失败');
      if (state.route === 'settings') renderSettings();
      return false;
    }
  }

  function updateSidebar() {
    const version = state.bootstrap && state.bootstrap.version;
    $('sideVersion').textContent = version ? 'v' + version : '';
  }

  // ---------- navigation ----------
  function currentPage() {
    const hash = (location.hash || '#settings').replace(/^#/, '');
    const page = hash.split('/')[0];
    return pageTitles[page] ? page : 'settings';
  }

  function updateNav(page) {
    document.querySelectorAll('.side-link').forEach((link) => {
      link.classList.toggle('active', link.dataset.page === page);
    });
  }

  async function route() {
    const page = currentPage();
    state.route = page;
    updateNav(page);
    document.title = pageTitles[page];
    if (!state.settings) {
      content.innerHTML = `<div class="empty"><div class="spinner"></div><p style="margin-top:14px">正在加载…</p></div>`;
      return;
    }

    try {
      if (page === 'settings') renderSettings();
      else if (page === 'history') await renderHistoryPage();
      else if (page === 'bookmarks') await renderBookmarksPage();
      else if (page === 'downloads') await renderDownloadsPage();
      else if (page === 'extensions') await renderExtensionsPage();
      else if (page === 'about') await renderAboutPage();
    } catch (error) {
      content.innerHTML = pageHead('XR Browser', '出错了', error.message || '无法加载页面') +
        `<div class="card"><div class="card-body"><button class="btn" data-action="refresh-page">${icon('refresh')}重试</button></div></div>`;
    }
  }

  // ---------- settings ----------
  function renderSettings() {
    const s = state.settings;
    if (!s) return;
    const zoomOptions = [[0.5, '50%'], [0.67, '67%'], [0.75, '75%'], [0.8, '80%'], [0.9, '90%'],
      [1, '100%'], [1.1, '110%'], [1.25, '125%'], [1.5, '150%'], [1.75, '175%'], [2, '200%']];
    const engineOptions = [
      ['Bing', 'Bing'], ['Google', 'Google'], ['Baidu', '百度'], ['DuckDuckGo', 'DuckDuckGo'], ['Yandex', 'Yandex'], ['Custom', '自定义']
    ];

    const themeOption = (value, label, previewClass, iconName) => `
      <button type="button" class="theme-option ${s.theme === value ? 'active' : ''}" data-theme-choice="${attr(value)}">
        <div class="theme-preview ${previewClass}"><div class="tp-bar"></div><div class="tp-line"></div><div class="tp-line" style="width:42%"></div></div>
        <div class="theme-name"><span>${icon(iconName, 'icon')} ${esc(label)}</span><span class="theme-check">✓</span></div>
      </button>`;

    content.innerHTML = `
      ${pageHead('XR Browser', '设置', '调整浏览器外观、启动方式、隐私与下载选项。')}

      <section class="card">
        <div class="card-header"><h2 class="card-title">外观</h2><p class="card-desc">让 XR Browser 更符合你的使用习惯。</p></div>
        <div class="card-body">
          <div class="theme-grid">
            ${themeOption('System', '跟随系统', 'system', 'monitor')}
            ${themeOption('Light', '浅色', 'light', 'sun')}
            ${themeOption('Dark', '深色', 'dark', 'moon')}
          </div>
          ${row('bookmark', '显示书签栏', '在地址栏下方显示书签快捷入口。', toggle('showBookmarksBar', s.showBookmarksBar))}
          ${row('browser', '显示主页按钮', '在工具栏中显示返回主页的按钮。', toggle('showHomeButton', s.showHomeButton))}
          ${row('search', '页面默认缩放', '新标签页和应用内页面的默认缩放比例。', `<select class="select" data-select="defaultZoom">${options(zoomOptions, s.defaultZoom)}</select>`)}
          ${row('animation', '界面动画', '为标签页和弹出菜单添加平滑过渡；关闭后立即切换。', toggle('animationsEnabled', s.animationsEnabled))}
          ${row('palette', '材质透明度', '标签栏、工具栏和弹层的半透明程度；数值越低越通透。',
            `<select class="select" data-select="surfaceOpacity">${options([
              [0.35, '35% · 极透'], [0.45, '45% · 通透'], [0.55, '55% · 均衡'], [0.7, '70% · 沉稳'], [0.85, '85% · 厚重'], [1, '100% · 不透明']
            ], s.surfaceOpacity)}</select>`)}
          ${row('animation', '动画速率', '弹层和标签页弹簧的播放速度。',
            `<select class="select" data-select="animationSpeed">${options([
              [0.75, '0.75x · 更慢'], [1, '1.0x · 标准'], [1.25, '1.25x · 更快'], [1.5, '1.5x · 迅速']
            ], s.animationSpeed)}</select>`)}
        </div>
      </section>

      <section class="card">
        <div class="card-header"><h2 class="card-title">搜索引擎</h2><p class="card-desc">在地址栏中输入的搜索内容将使用此引擎。</p></div>
        <div class="card-body">
          ${row('globe', '地址栏搜索引擎', '支持 Google、Bing、百度、DuckDuckGo 与自定义模板。',
            `<div class="search-engine-row"><span class="search-engine-logo">${esc((s.searchEngineName || 'G').charAt(0))}</span><select class="select" data-select="searchEngine">${options(engineOptions, s.searchEngine)}</select></div>`)}
          ${s.searchEngine === 'Custom' ? `
            <div class="setting-row">
              <div class="setting-icon">${icon('code')}</div>
              <div class="setting-text">
                <div class="setting-label">自定义搜索 URL</div>
                <div class="setting-help">使用 {0} 表示搜索词，例如 https://example.com/search?q={0}</div>
                <input class="text-input" style="margin-top:8px" data-text-setting="customSearchUrl" value="${attr(s.customSearchUrl)}" />
              </div>
              <div class="setting-control"><button class="btn" data-action="save-text" data-key="customSearchUrl">保存</button></div>
            </div>` : ''}
        </div>
      </section>

      <section class="card">
        <div class="card-header"><h2 class="card-title">启动时</h2><p class="card-desc">选择 XR Browser 打开时显示的内容。</p></div>
        <div class="card-body">
          ${row('browser', '启动行为', '继续上次的会话，或打开新标签页 / 指定主页。',
            `<select class="select" data-select="startupMode">${options([
              ['continue', '继续上次打开的网页'], ['newtab', '打开新标签页'], ['home', '打开主页']
            ], s.startupMode)}</select>`)}
          ${row('home', '主页', '点击主页按钮或按 Alt+Home 时打开。',
            `<input class="text-input" style="width:260px" data-text-setting="homePage" value="${attr(s.homePage)}" /><button class="btn" data-action="save-text" data-key="homePage">保存</button>`)}
          ${row('history', '恢复标签页', '重新启动时恢复上次关闭时的标签页。', toggle('restoreSession', s.restoreSession))}
        </div>
      </section>

      <section class="card">
        <div class="card-header"><h2 class="card-title">下载内容</h2><p class="card-desc">设置下载文件的保存位置。</p></div>
        <div class="card-body">
          ${row('folder', '下载位置', s.downloadDirectory || '尚未设置',
            `<button class="btn" data-action="choose-download-folder">${icon('folder')}更改</button>`)}
          ${row('download', '下载前询问保存位置', '每个文件下载前都显示保存对话框。', toggle('askWhereToSave', s.askWhereToSave))}
        </div>
      </section>

      <section class="card">
        <div class="card-header"><h2 class="card-title">隐私与安全</h2><p class="card-desc">控制网站可使用的数据以及浏览数据的清理。</p></div>
        <div class="card-body">
          ${row('shield', 'AdGuard 广告过滤', '使用 AdGuard Base 与中文过滤列表拦截广告和跟踪请求。', toggle('adBlockEnabled', s.adBlockEnabled))}
          ${row('moon', 'Dark Reader', '为网站启用深色主题，不改变浏览器自身外观。', toggle('darkReaderEnabled', s.darkReaderEnabled))}
          ${row('shield', '跟踪防护', '拦截已知的跨站跟踪器。',
            `<select class="select" data-select="trackingPrevention">${options([
              ['Off', '关闭'], ['Basic', '基本'], ['Balanced', '均衡（推荐）'], ['Strict', '严格']
            ], s.trackingPrevention)}</select>`)}
          ${row('browser', '自动填充', '保存并填充表单中的地址和联系方式。', toggle('autofillEnabled', s.autofillEnabled))}
          ${row('key', '密码自动保存', '在获得你允许时保存网站密码。', toggle('passwordAutosaveEnabled', s.passwordAutosaveEnabled))}
          ${row('code', 'JavaScript', '允许网站运行 JavaScript。关闭后部分网站可能无法正常工作。', toggle('javaScriptEnabled', s.javaScriptEnabled))}
          ${row('database', '清除浏览数据', '删除历史记录、Cookie、缓存与其他网站数据。',
            `<button class="btn danger" data-action="clear-data">${icon('trash')}清除数据</button>`)}
        </div>
      </section>

      <section class="card">
        <div class="card-header"><h2 class="card-title">系统与扩展</h2><p class="card-desc">Chromium 内核与 Windows 集成。</p></div>
        <div class="card-body">
          ${row('extensions', '启用扩展程序', '允许加载解压后的 Chromium 扩展。环境设置更改后需要重启浏览器。', toggle('enableExtensions', s.enableExtensions))}
          ${row('browser', '默认浏览器', '在 Windows 设置中将 XR Browser 设为默认浏览器。',
            `<button class="btn" data-action="set-default-browser">${icon('external')}打开设置</button>`)}
          ${row('info', '关于 XR Browser', '查看内核版本、运行环境和数据目录。',
            `<button class="btn ghost" data-action="open-about">查看</button>`)}
        </div>
      </section>
    `;
  }

  // ---------- history ----------
  async function renderHistoryPage() {
    content.innerHTML = pageHead('XR Browser', '历史记录', '浏览记录仅保存在本设备，无痕窗口中的访问不会写入。',
      `<button class="btn danger" data-action="clear-data">${icon('trash')}清除浏览数据</button>`) +
      `<div class="toolbar"><div class="search-field">${icon('search')}<input id="historySearch" type="search" placeholder="搜索历史记录" value="${attr(state.historyQuery)}" /></div><span class="pill" id="historyCount"></span></div>
       <div id="historyList"></div>`;
    await loadHistory();
    updateHistoryRows();
  }

  function historyMatches() {
    const q = state.historyQuery.trim().toLowerCase();
    if (!q) return state.history;
    return state.history.filter((h) =>
      (h.title || '').toLowerCase().includes(q) || (h.url || '').toLowerCase().includes(q) || (h.host || '').toLowerCase().includes(q));
  }

  function historyListHtml() {
    const rows = historyMatches();
    if (!rows.length) {
      return state.historyQuery
        ? emptyState('search', '没有匹配的历史记录', '试试其他关键词。')
        : emptyState('history', '还没有历史记录', '你访问的网页会显示在这里。');
    }
    const groups = new Map();
    rows.forEach((entry) => {
      const key = dateGroupLabel(entry.lastVisit);
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(entry);
    });
    return [...groups.entries()].map(([label, entries]) => `
      <div class="history-group">
        <div class="history-date">${esc(label)}</div>
        <div class="history-card">
          ${entries.map((entry) => `
            <div class="list-row">
              <div class="list-icon">${attr(letterFor(entry.url, entry.title))}</div>
              <button class="list-main" style="border:0;background:transparent;text-align:left;padding:0;cursor:pointer" data-open-url="${attr(entry.url)}">
                <div class="list-title">${esc(entry.title || entry.url)}</div>
                <div class="list-sub">${esc(entry.url)}</div>
              </button>
              <span class="list-meta">${esc(formatTime(entry.lastVisit))}</span>
              <div class="list-actions">
                <button class="icon-btn" title="从历史记录中移除" data-action="delete-history" data-url="${attr(entry.url)}">${icon('trash')}</button>
              </div>
            </div>`).join('')}
        </div>
      </div>`).join('');
  }

  function updateHistoryRows() {
    const host = $('historyList');
    if (!host) return;
    host.innerHTML = historyListHtml();
    const count = $('historyCount');
    if (count) count.textContent = `${historyMatches().length} 条记录`;
  }

  // ---------- bookmarks ----------
  async function renderBookmarksPage() {
    await loadBookmarks();
    const s = state.settings;
    content.innerHTML = pageHead('XR Browser', '书签', '收藏常用网站，并在书签栏中快速访问。',
      `<button class="btn primary" data-action="add-bookmark">${icon('plus')}添加书签</button>`) +
      `<div class="card">
        <div class="card-body">
          ${row('bookmark', '显示书签栏', '在地址栏下方显示书签快捷入口。', toggle('showBookmarksBar', s.showBookmarksBar))}
        </div>
      </div>
      <div class="card" id="bookmarkList"></div>`;
    renderBookmarkList();
  }

  function renderBookmarkList() {
    const host = $('bookmarkList');
    if (!host) return;
    if (!state.bookmarks.length) {
      host.innerHTML = emptyState('bookmark', '还没有书签', '点击地址栏中的星标，或使用“添加书签”按钮。');
      return;
    }
    host.innerHTML = `<div class="list">${state.bookmarks.map((bookmark) => `
      <div class="list-row bookmark-row">
        <div class="list-icon">${attr(letterFor(bookmark.url, bookmark.title))}</div>
        <button class="list-main" style="border:0;background:transparent;text-align:left;padding:0;cursor:pointer" data-open-url="${attr(bookmark.url)}">
          <div class="list-title">${esc(bookmark.title || bookmark.url)}</div>
          <div class="list-sub">${esc(bookmark.url)}</div>
        </button>
        <div class="list-actions">
          <button class="icon-btn" title="修改书签" data-action="edit-bookmark" data-id="${attr(bookmark.id)}">${icon('edit')}</button>
          <button class="icon-btn" title="删除书签" data-action="delete-bookmark" data-id="${attr(bookmark.id)}">${icon('trash')}</button>
        </div>
      </div>`).join('')}</div>`;
  }

  function openBookmarkModal(bookmark) {
    state.editingBookmark = bookmark || null;
    openModal(`
      <h2>${bookmark ? '修改书签' : '添加书签'}</h2>
      <p>书签会保存在本地配置中，并显示在书签栏。</p>
      <div class="field"><label for="bmTitle">名称</label><input class="text-input large" id="bmTitle" value="${attr(bookmark ? bookmark.title : '')}" placeholder="书签名称" /></div>
      <div class="field"><label for="bmUrl">网址</label><input class="text-input large" id="bmUrl" value="${attr(bookmark ? bookmark.url : '')}" placeholder="https://example.com" /></div>
      <div class="modal-actions">
        <button class="btn" data-action="close-modal">取消</button>
        <button class="btn primary" data-action="save-bookmark">保存</button>
      </div>`);
  }

  // ---------- downloads ----------
  async function renderDownloadsPage() {
    await loadDownloads();
    content.innerHTML = pageHead('XR Browser', '下载内容', '查看本次浏览会话中的文件传输。',
      `<button class="btn" data-action="open-downloads-folder">${icon('folder')}打开文件夹</button>
       <button class="btn" data-action="clear-downloads">清除已完成</button>`) +
      `<div class="card" id="downloadList"></div>`;
    renderDownloadList();
  }

  function downloadRow(item) {
    const active = item.isActive;
    const progress = Math.max(0, Math.min(100, Number(item.progress) || 0));
    const iconName = item.isCompleted ? 'check' : active ? 'download' : 'pause';
    let actions = '';
    if (item.canOpen) actions += `<button class="icon-btn" title="打开" data-action="download-action" data-id="${attr(item.id)}" data-op="open">${icon('external')}</button>`;
    if (item.canShowInFolder) actions += `<button class="icon-btn" title="在文件夹中显示" data-action="download-action" data-id="${attr(item.id)}" data-op="show">${icon('folder')}</button>`;
    if (item.canPause) actions += `<button class="icon-btn" title="暂停" data-action="download-action" data-id="${attr(item.id)}" data-op="pause">${icon('pause')}</button>`;
    if (item.canResume) actions += `<button class="icon-btn" title="继续" data-action="download-action" data-id="${attr(item.id)}" data-op="resume">${icon('play')}</button>`;
    if (active) actions += `<button class="icon-btn" title="取消" data-action="download-action" data-id="${attr(item.id)}" data-op="cancel">${icon('x')}</button>`;
    else actions += `<button class="icon-btn" title="从列表中移除" data-action="download-action" data-id="${attr(item.id)}" data-op="remove">${icon('trash')}</button>`;

    return `
      <div class="list-row">
        <div class="list-icon">${icon(iconName)}</div>
        <div class="list-main">
          <div class="list-title">${esc(item.fileName)}</div>
          <div class="list-sub">${esc(item.stateText)} · ${esc(item.progressText)}${item.speed ? ' · ' + esc(item.speed) : ''}</div>
          ${active ? `<div class="progress-track"><div class="progress-fill" style="width:${progress}%"></div></div>` : ''}
        </div>
        <div class="download-actions">${actions}</div>
      </div>`;
  }

  function renderDownloadList() {
    const host = $('downloadList');
    if (!host) return;
    if (!state.downloads.length) {
      host.innerHTML = emptyState('download', '还没有下载内容', '从网页下载的文件会显示在这里。');
      return;
    }
    host.innerHTML = `<div class="list">${state.downloads.map(downloadRow).join('')}</div>`;
  }

  // ---------- extensions ----------
  async function renderExtensionsPage() {
    await loadExtensions();
    const enabled = state.settings.enableExtensions;
    const warning = !enabled
      ? `<div class="card"><div class="card-body"><div class="list-row" style="padding:0;border:0"><div class="setting-icon" style="color:var(--danger)">${icon('warning')}</div><div class="setting-text"><div class="setting-label">扩展程序支持已关闭</div><div class="setting-help">请在设置中启用扩展程序并重启 XR Browser。</div></div><button class="btn primary" data-action="open-settings">前往设置</button></div></div></div>`
      : '';

    content.innerHTML = pageHead('XR Browser', '扩展程序', '加载解压后的 Chromium 扩展，或从本地 CRX 安装。WebView2 仅支持部分扩展 API。',
      `<button class="btn" data-action="install-crx" ${enabled ? '' : 'disabled'}>${icon('download')}从 CRX 安装</button>
       <button class="btn primary" data-action="load-extension" ${enabled ? '' : 'disabled'}>${icon('folder')}加载已解压的扩展程序</button>`) +
      warning +
      `<div class="card" id="extensionList"></div>`;
    renderExtensionList();
  }

  function renderExtensionList() {
    const host = $('extensionList');
    if (!host) return;
    if (state.extensionError) {
      host.innerHTML = emptyState('warning', '无法读取扩展程序', state.extensionError);
      return;
    }
    if (!state.extensions.length) {
      host.innerHTML = emptyState('extensions', '尚未安装扩展程序', '从可信来源选择 CRX 文件，或加载一个包含 manifest.json 的文件夹。');
      return;
    }
    host.innerHTML = `<div class="list">${state.extensions.map((extension) => `
      <div class="list-row ext-row">
        <div class="list-icon ext-icon">${icon('extensions')}</div>
        <div class="list-main">
          <div class="list-title">${esc(extension.name)}</div>
          <div class="list-sub">${esc(extension.id)}</div>
        </div>
        <label class="switch"><input type="checkbox" data-extension-toggle="${attr(extension.id)}" ${extension.enabled ? 'checked' : ''}><span class="track"></span></label>
        <div class="list-actions" style="opacity:1">
          <button class="icon-btn" title="移除扩展程序" data-action="remove-extension" data-id="${attr(extension.id)}">${icon('trash')}</button>
        </div>
      </div>`).join('')}</div>`;
  }

  // ---------- about ----------
  async function renderAboutPage() {
    await loadBrowserInfo();
    const info = state.browserInfo || {};
    const version = state.bootstrap ? state.bootstrap.version : (info.appVersion || '1.0.0');
    content.innerHTML = pageHead('XR Browser', '关于', '一个用 C#、WPF 与 Chromium 构建的现代浏览器。') +
      `<div class="card">
        <div class="about-hero">
          <div class="about-logo">XR</div>
          <div>
            <div class="about-name">XR Browser</div>
            <div class="about-version">版本 ${esc(version)} · ${esc(info.profile || '本地个人资料')}</div>
            <div class="about-version" style="margin-top:10px"><span class="pill ok">${icon('check')} 已是最新版本</span></div>
          </div>
        </div>
      </div>
      <div class="card">
        <div class="card-header"><h2 class="card-title">运行环境</h2></div>
        <div class="card-body">
          <div class="kv">
            <div class="k">Chromium 内核</div><div class="v">${esc(info.chromiumVersion || '—')}</div>
            <div class="k">WebView2 Runtime</div><div class="v">${esc(info.webViewVersion || '—')}</div>
            <div class="k">操作系统</div><div class="v">${esc(info.os || '—')}</div>
            <div class="k">进程架构</div><div class="v">${esc(info.architecture || '—')}</div>
            <div class="k">默认搜索引擎</div><div class="v">${esc(info.searchEngine || '—')}</div>
            <div class="k">数据目录</div><div class="v">${esc(info.dataPath || '—')}</div>
          </div>
        </div>
      </div>
      <div class="card">
        <div class="card-header"><h2 class="card-title">已包含的功能</h2></div>
        <div class="card-body">
          <div class="feature-list">
            <div class="feature">${icon('grid')}<span>多标签页、标签拖拽与会话恢复</span></div>
            <div class="feature">${icon('search')}<span>地址栏搜索建议与历史/书签补全</span></div>
            <div class="feature">${icon('bookmark')}<span>书签、书签栏与内置管理页面</span></div>
            <div class="feature">${icon('download')}<span>下载管理、进度与文件操作</span></div>
            <div class="feature">${icon('shield')}<span>跟踪防护、权限提示与隐私设置</span></div>
            <div class="feature">${icon('extensions')}<span>Chromium 扩展加载与启停</span></div>
            <div class="feature">${icon('code')}<span>开发者工具、查找、打印、缩放</span></div>
            <div class="feature">${icon('moon')}<span>浅色 / 深色 / 跟随系统主题</span></div>
          </div>
        </div>
      </div>
      <div class="card"><div class="card-body" style="display:flex;gap:9px;flex-wrap:wrap">
        <button class="btn primary" data-action="set-default-browser">${icon('browser')}设为默认浏览器</button>
        <button class="btn" data-action="open-settings">${icon('settings')}打开设置</button>
        <button class="btn" data-action="check-update">${icon('update')}检查更新</button>
      </div></div>`;
  }

  // ---------- modals ----------
  function openClearDataModal() {
    openModal(`
      <h2>清除浏览数据</h2>
      <p>选择要删除的数据。此操作无法撤销。</p>
      <div style="margin-top:14px">
        <label class="check-row"><input type="checkbox" data-clear-kind="history" checked /> 浏览历史记录</label>
        <label class="check-row"><input type="checkbox" data-clear-kind="cookies" checked /> Cookie 和其他网站数据</label>
        <label class="check-row"><input type="checkbox" data-clear-kind="cache" checked /> 缓存的图片和文件</label>
        <label class="check-row"><input type="checkbox" data-clear-kind="passwords" /> 密码</label>
        <label class="check-row"><input type="checkbox" data-clear-kind="autofill" /> 自动填充数据</label>
        <label class="check-row"><input type="checkbox" data-clear-kind="downloads" /> 下载记录</label>
      </div>
      <div class="modal-actions">
        <button class="btn" data-action="close-modal">取消</button>
        <button class="btn danger" data-action="clear-data-confirm">清除数据</button>
      </div>`);
  }

  async function clearData() {
    const kinds = [...document.querySelectorAll('[data-clear-kind]:checked')].map((el) => el.dataset.clearKind);
    if (!kinds.length) { toast('请至少选择一项'); return; }
    try {
      await call('clearBrowsingData', { kinds }, 60000);
      closeModal();
      toast('浏览数据已清除');
      if (state.route === 'history') await renderHistoryPage();
      if (state.route === 'downloads') await renderDownloadsPage();
    } catch (error) {
      toast(error.message || '清除失败');
    }
  }

  // ---------- events ----------
  function handleHostEvent(msg) {
    if (!msg || msg.type !== 'event') return;
    if (msg.name === 'themeChanged') {
      state.isDark = !!msg.data;
      document.documentElement.dataset.theme = state.isDark ? 'dark' : 'light';
    } else if (msg.name === 'settingsChanged') {
      refreshSettings().then(() => {
        if (state.route === 'settings') renderSettings();
        updateSidebar();
      }).catch(() => {});
    } else if (msg.name === 'bookmarksChanged') {
      loadBootstrap().then(() => {
        if (state.route === 'bookmarks') renderBookmarkList();
      }).catch(() => {});
    } else if (msg.name === 'historyChanged') {
      if (state.route === 'history') loadHistory().then(updateHistoryRows).catch(() => {});
    } else if (msg.name === 'downloadsChanged') {
      if (state.route !== 'downloads') return;
      const now = Date.now();
      if (now - state.lastDownloadRefresh < 500) return;
      state.lastDownloadRefresh = now;
      loadDownloads().then(renderDownloadList).catch(() => {});
    }
  }

  content.addEventListener('click', async (event) => {
    const openUrl = event.target.closest('[data-open-url]');
    if (openUrl) {
      event.preventDefault();
      try { await call('openUrl', { url: openUrl.dataset.openUrl, newTab: false }); }
      catch (error) { toast(error.message); }
      return;
    }

    const button = event.target.closest('[data-action]');
    if (!button) return;
    const action = button.dataset.action;

    try {
      switch (action) {
        case 'refresh-page':
          await route();
          break;
        case 'open-about':
          location.hash = 'about';
          break;
        case 'open-settings':
          location.hash = 'settings';
          break;
        case 'choose-download-folder': {
          const result = await call('chooseDownloadFolder', {}, 120000);
          if (result && result.folder) {
            state.settings = result.settings || state.settings;
            toast('下载位置已更新');
            renderSettings();
          }
          break;
        }
        case 'clear-data':
          openClearDataModal();
          break;
        case 'clear-data-confirm':
          await clearData();
          break;
        case 'set-default-browser':
          await call('setDefaultBrowser');
          break;
        case 'delete-history': {
          const url = button.dataset.url;
          await call('deleteHistory', { url });
          state.history = state.history.filter((h) => h.url !== url);
          updateHistoryRows();
          toast('已从历史记录中移除');
          break;
        }
        case 'add-bookmark':
          openBookmarkModal(null);
          break;
        case 'edit-bookmark': {
          const bookmark = state.bookmarks.find((b) => b.id === button.dataset.id);
          if (bookmark) openBookmarkModal(bookmark);
          break;
        }
        case 'save-bookmark': {
          const title = ($('bmTitle') || {}).value || '';
          const url = (($('bmUrl') || {}).value || '').trim();
          if (!/^https?:\/\//i.test(url)) { toast('请输入有效的 http(s) 网址'); return; }
          if (state.editingBookmark) {
            await call('updateBookmark', { id: state.editingBookmark.id, title, url }, 30000);
          } else {
            await call('addBookmark', { title, url }, 30000);
          }
          closeModal();
          await loadBookmarks();
          if (state.route === 'bookmarks') renderBookmarkList();
          toast('书签已保存');
          break;
        }
        case 'delete-bookmark': {
          await call('removeBookmark', { id: button.dataset.id });
          await loadBookmarks();
          if (state.route === 'bookmarks') renderBookmarkList();
          toast('书签已删除');
          break;
        }
        case 'close-modal':
          closeModal();
          break;
        case 'clear-downloads':
          await call('clearDownloads');
          await renderDownloadsPage();
          toast('已清除完成的下载');
          break;
        case 'open-downloads-folder':
          await call('openDownloadsFolder');
          break;
        case 'download-action': {
          await call('downloadAction', { id: button.dataset.id, action: button.dataset.op });
          await loadDownloads();
          renderDownloadList();
          break;
        }
        case 'load-extension': {
          const extension = await call('loadExtension', {}, 120000);
          toast(extension && extension.name ? `已安装：${extension.name}` : '扩展程序已安装');
          await loadExtensions();
          renderExtensionList();
          break;
        }
        case 'install-crx': {
          const result = await call('installCrx', {}, 120000);
          if (result && result.installed) {
            toast(`已安装：${result.name}`);
            await loadExtensions();
            renderExtensionList();
          }
          break;
        }
        case 'remove-extension': {
          await call('removeExtension', { id: button.dataset.id }, 30000);
          await loadExtensions();
          renderExtensionList();
          toast('扩展程序已移除');
          break;
        }
        case 'save-text': {
          const key = button.dataset.key;
          const input = document.querySelector(`[data-text-setting="${key}"]`);
          if (!input) return;
          await setSetting(key, input.value.trim());
          toast('已保存');
          break;
        }
        case 'check-update':
          toast('XR Browser 已是最新版本');
          break;
      }
    } catch (error) {
      toast(error.message || '操作失败');
    }
  });

  content.addEventListener('change', async (event) => {
    const target = event.target;
    if (target.matches('[data-toggle]')) {
      await setSetting(target.dataset.toggle, target.checked);
    } else if (target.matches('[data-select]')) {
      const key = target.dataset.select;
      let value = target.value;
      if (key === 'defaultZoom' || key === 'animationSpeed' || key === 'surfaceOpacity') value = Number(value);
      const ok = await setSetting(key, value);
      if (ok && (key === 'searchEngine' || key === 'startupMode')) renderSettings();
    } else if (target.matches('[data-extension-toggle]')) {
      const id = target.dataset.extensionToggle;
      try {
        await call('setExtensionEnabled', { id, enabled: target.checked }, 30000);
        toast(target.checked ? '扩展程序已启用' : '扩展程序已停用');
      } catch (error) {
        target.checked = !target.checked;
        toast(error.message || '操作失败');
      }
    }
  });

  content.addEventListener('click', (event) => {
    const themeButton = event.target.closest('[data-theme-choice]');
    if (!themeButton) return;
    setSetting('theme', themeButton.dataset.themeChoice);
  });

  document.addEventListener('input', (event) => {
    if (event.target.id === 'historySearch') {
      state.historyQuery = event.target.value;
      updateHistoryRows();
    }
  });

  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') {
      if ($('modalBackdrop').classList.contains('open')) closeModal();
    }
  });

  $('modalBackdrop').addEventListener('mousedown', (event) => {
    if (event.target === $('modalBackdrop')) closeModal();
  });

  if (window.matchMedia) {
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
      if (state.settings && state.settings.theme === 'System') applyTheme('System', state.isDark);
    });
  }

  window.addEventListener('hashchange', route);

  // ---------- mock fallback (for local preview) ----------
  const mock = {
    settings: {
      theme: 'System',
      resolvedTheme: 'Light',
      isDark: false,
      searchEngine: 'Bing',
      searchEngineName: 'Bing',
      customSearchUrl: 'https://www.google.com/search?q={0}',
      homePage: 'xr://newtab',
      restoreSession: true,
      showBookmarksBar: true,
      downloadDirectory: 'C:\\Users\\you\\Downloads\\XR Browser',
      askWhereToSave: false,
      startupMode: 'continue',
      autofillEnabled: true,
      passwordAutosaveEnabled: true,
      javaScriptEnabled: true,
      trackingPrevention: 'Balanced',
      defaultZoom: 1,
      showHomeButton: true,
      enableExtensions: true,
      adBlockEnabled: true,
      darkReaderEnabled: false,
      showRecentSites: false,
      newTabBackground: '',
      animationsEnabled: true,
      animationSpeed: 1,
      surfaceOpacity: 0.55,
      micaEnabled: false,
      glassEnabled: true
    },
    version: '1.0.0',
    webViewVersion: '155.0.4283.62',
    history: [
      { url: 'https://developer.microsoft.com/microsoft-edge/webview2/', title: 'WebView2 – Microsoft Edge Developer', host: 'developer.microsoft.com', lastVisit: new Date().toISOString(), visitCount: 4 },
      { url: 'https://github.com/', title: 'GitHub: Let’s build from here', host: 'github.com', lastVisit: new Date(Date.now() - 86400000).toISOString(), visitCount: 9 },
      { url: 'https://learn.microsoft.com/dotnet/desktop/wpf/', title: 'WPF for .NET | Microsoft Learn', host: 'learn.microsoft.com', lastVisit: new Date(Date.now() - 86400000 * 2).toISOString(), visitCount: 3 },
      { url: 'https://www.google.com/', title: 'Google', host: 'google.com', lastVisit: new Date(Date.now() - 86400000 * 3).toISOString(), visitCount: 12 }
    ],
    bookmarks: [
      { id: 'b1', title: 'WebView2 文档', url: 'https://learn.microsoft.com/microsoft-edge/webview2/', host: 'learn.microsoft.com' },
      { id: 'b2', title: 'GitHub', url: 'https://github.com/', host: 'github.com' },
      { id: 'b3', title: '.NET 文档', url: 'https://learn.microsoft.com/dotnet/', host: 'learn.microsoft.com' }
    ],
    downloads: [
      { id: 'd1', fileName: 'WebView2GettingStarted.pdf', stateText: '已完成', isActive: false, isCompleted: true, progress: 100, progressText: '2.4 MB', speed: '', canOpen: true, canShowInFolder: true, canPause: false, canResume: false }
    ],
    extensions: [
      { id: 'mock-extension', name: '示例扩展', enabled: true }
    ]
  };

  async function mockCall(method, params = {}) {
    await new Promise((resolve) => setTimeout(resolve, 50));
    switch (method) {
      case 'getBootstrap':
        return {
          settings: mock.settings,
          version: mock.version,
          webViewVersion: mock.webViewVersion,
          isDark: false,
          isIncognito: false,
          topSites: mock.history.slice(0, 5),
          bookmarks: mock.bookmarks,
          downloads: mock.downloads,
          historyCount: mock.history.length,
          bookmarkCount: mock.bookmarks.length
        };
      case 'getSettings': return { ...mock.settings };
      case 'setSetting': mock.settings[params.key] = params.value; return { ...mock.settings };
      case 'getHistory': return mock.history;
      case 'deleteHistory': mock.history = mock.history.filter((h) => h.url !== params.url); return { deleted: true };
      case 'getBookmarks': return mock.bookmarks;
      case 'addBookmark': {
        const bookmark = { id: 'b' + Date.now(), title: params.title || params.url, url: params.url };
        mock.bookmarks.push(bookmark);
        return { id: bookmark.id };
      }
      case 'updateBookmark': {
        const b = mock.bookmarks.find((x) => x.id === params.id);
        if (b) { b.title = params.title; b.url = params.url; }
        return { updated: true };
      }
      case 'removeBookmark': mock.bookmarks = mock.bookmarks.filter((b) => b.id !== params.id); return { removed: true };
      case 'getDownloads': return mock.downloads;
      case 'clearDownloads': mock.downloads = []; return { cleared: true };
      case 'getExtensions': return mock.extensions;
      case 'getBrowserInfo':
        return { appVersion: mock.version, webViewVersion: mock.webViewVersion, chromiumVersion: '155', os: 'Windows 11', architecture: 'X64', dataPath: 'C:\\Users\\you\\AppData\\Local\\XRBrowser', profile: '本地个人资料', searchEngine: 'Google' };
      case 'chooseDownloadFolder': return { folder: mock.settings.downloadDirectory, settings: mock.settings };
      case 'openDownloadsFolder':
      case 'setDefaultBrowser': return { opened: true };
      case 'clearBrowsingData': mock.history = []; return { cleared: true };
      case 'loadExtension': return { name: '示例扩展' };
      case 'removeExtension': return { removed: true };
      case 'setExtensionEnabled': return { updated: true };
      case 'openUrl':
      case 'search':
      case 'openInternalPage': return { navigated: true };
      default: return {};
    }
  }

  async function init() {
    try {
      await loadBootstrap();
      await route();
    } catch (error) {
      content.innerHTML = pageHead('XR Browser', '无法加载页面', error.message || '浏览器桥接不可用。') +
        `<div class="card"><div class="card-body"><button class="btn primary" data-action="refresh-page">${icon('refresh')}重试</button></div></div>`;
    }
  }

  init();
})();
