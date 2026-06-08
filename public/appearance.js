(function () {
  'use strict';

  const STORAGE_KEY = 'ateq-appearance-mode';
  const CHANGE_KEY = 'ateq-appearance-updated';
  const MODES = new Set(['system', 'light', 'dark']);
  const mediaQuery = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

  function normalizeMode(mode) {
    const normalized = String(mode || '').trim().toLowerCase();
    return MODES.has(normalized) ? normalized : 'system';
  }

  function getStoredMode() {
    try {
      return normalizeMode(localStorage.getItem(STORAGE_KEY));
    } catch (error) {
      return 'system';
    }
  }

  function resolveTheme(mode) {
    const normalized = normalizeMode(mode);
    if (normalized === 'system') {
      return mediaQuery && mediaQuery.matches ? 'dark' : 'light';
    }
    return normalized;
  }

  function applyMode(mode) {
    const normalized = normalizeMode(mode);
    const theme = resolveTheme(normalized);
    document.documentElement.dataset.appearance = normalized;
    document.documentElement.dataset.theme = theme;
    return { mode: normalized, theme };
  }

  function setMode(mode) {
    const normalized = normalizeMode(mode);
    try {
      localStorage.setItem(STORAGE_KEY, normalized);
      localStorage.setItem(CHANGE_KEY, JSON.stringify({
        mode: normalized,
        at: new Date().toISOString()
      }));
    } catch (error) {
      // localStorage can be unavailable in kiosk/browser lockdown modes.
    }
    return applyMode(normalized);
  }

  function handleSystemChange() {
    if (getStoredMode() === 'system') {
      applyMode('system');
    }
  }

  if (mediaQuery) {
    if (typeof mediaQuery.addEventListener === 'function') {
      mediaQuery.addEventListener('change', handleSystemChange);
    } else if (typeof mediaQuery.addListener === 'function') {
      mediaQuery.addListener(handleSystemChange);
    }
  }

  window.addEventListener('storage', (event) => {
    if (event.key === STORAGE_KEY || event.key === CHANGE_KEY) {
      applyMode(getStoredMode());
    }
  });

  window.Appearance = {
    STORAGE_KEY,
    CHANGE_KEY,
    getMode: getStoredMode,
    setMode,
    applyMode,
    resolveTheme
  };

  applyMode(getStoredMode());
}());
