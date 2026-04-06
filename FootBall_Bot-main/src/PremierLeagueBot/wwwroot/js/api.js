const Api = (() => {
  let _initData = '';

  function setInitData(data) { _initData = data; }

  async function request(path, opts = {}) {
    const res = await fetch(path, {
      ...opts,
      headers: {
        'Content-Type': 'application/json',
        'X-Telegram-Init-Data': _initData,
        ...(opts.headers || {})
      }
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: res.statusText }));
      throw Object.assign(new Error(err.error || 'API error'), { status: res.status, data: err });
    }
    return res.json();
  }

  return {
    setInitData,
    login:       (initData)           => fetch('/api/auth/login', {
                                          method: 'POST',
                                          headers: { 'Content-Type': 'application/json' },
                                          body: JSON.stringify({ initData })
                                        }).then(r => r.json()),
    me:          ()                   => request('/api/user/me'),
    user:        (id)                 => request(`/api/user/${id}`),
    standings:   ()                   => request('/api/standings'),
    upcoming:    ()                   => request('/api/matches/upcoming'),
    team:        (id)                 => request(`/api/teams/${id}`),
    predictions: ()                   => request('/api/predictions'),
    savePred:    (matchId, h, a)      => request('/api/predictions', {
                                          method: 'POST',
                                          body: JSON.stringify({ matchId, homeScore: h, awayScore: a })
                                        }),
  };
})();
