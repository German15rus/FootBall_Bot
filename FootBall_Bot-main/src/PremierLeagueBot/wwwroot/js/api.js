const Api = (() => {
  let _initData    = '';
  let _sessionToken = '';

  const SESSION_KEY = 'tg_session_token';

  function setInitData(data) { _initData = data; }

  function setSessionToken(token) {
    _sessionToken = token || '';
    try { localStorage.setItem(SESSION_KEY, _sessionToken); } catch(_) {}
  }

  function loadSessionToken() {
    try { _sessionToken = localStorage.getItem(SESSION_KEY) || ''; } catch(_) {}
  }

  async function request(path, opts = {}) {
    const headers = { 'Content-Type': 'application/json', ...(opts.headers || {}) };
    if (_sessionToken) headers['X-Session-Token']        = _sessionToken;
    if (_initData)     headers['X-Telegram-Init-Data']  = _initData;

    const res = await fetch(path, { ...opts, headers });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: res.statusText }));
      throw Object.assign(new Error(err.error || 'API error'), { status: res.status, data: err });
    }
    return res.json();
  }

  return {
    setInitData,
    setSessionToken,
    loadSessionToken,
    login:       (initData)           => fetch('/api/auth/login', {
                                          method: 'POST',
                                          headers: { 'Content-Type': 'application/json' },
                                          body: JSON.stringify({ initData })
                                        }).then(r => r.json()),
    me:          ()                   => request('/api/user/me'),
    user:        (id)                 => request(`/api/user/${id}`),
    standings:   ()                   => request('/api/standings'),
    upcoming:    (league = 'epl')      => request(`/api/matches/upcoming?league=${league}`),
    team:        (id)                 => request(`/api/teams/${id}`),
    predictions: ()                   => request('/api/predictions'),
    savePred:    (matchId, h, a)      => request('/api/predictions', {
                                          method: 'POST',
                                          body: JSON.stringify({ matchId, homeScore: h, awayScore: a })
                                        }),
  };
})();
