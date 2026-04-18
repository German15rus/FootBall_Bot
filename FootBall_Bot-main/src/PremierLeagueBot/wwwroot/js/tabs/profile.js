const ProfileTab = (() => {
  let _data = null;
  let _isOwnProfile = true;

  // ── Shop ───────────────────────────────────────────────────────────────────

  const PRIZES = [
    { icon: '🎽', nameRu: 'Футболка любимой команды', nameEn: 'Favourite club shirt',  cost: 500  },
    { icon: '🎮', nameRu: 'FIFA 25',                  nameEn: 'FIFA 25',               cost: 800  },
    { icon: '⚽', nameRu: 'Мяч с автографом',         nameEn: 'Signed match ball',     cost: 1000 },
    { icon: '🎟️', nameRu: 'Билет на матч',            nameEn: 'Match ticket',          cost: 2000 },
    { icon: '🏆', nameRu: 'Кубок болельщика',         nameEn: 'Fan trophy',            cost: 5000 },
  ];

  function ensureShopModal() {
    if (document.getElementById('shop-modal')) return;
    const t = i18n.t.bind(i18n);

    const prizesHtml = PRIZES.map(p => {
      const name = i18n.lang === 'ru' ? p.nameRu : p.nameEn;
      return `<div class="prize-card">
        <div class="prize-icon">${p.icon}</div>
        <div class="prize-info">
          <div class="prize-name">${escHtml(name)}</div>
          <div class="prize-cost">${p.cost} ${t('shop.points')}</div>
        </div>
        <button class="prize-exchange-btn" data-exchange="1">${t('shop.exchange')}</button>
      </div>`;
    }).join('');

    const el = document.createElement('div');
    el.innerHTML = `
      <div id="shop-modal" class="modal hidden">
        <div class="modal-backdrop" id="shop-backdrop"></div>
        <div class="modal-sheet">
          <div class="modal-header">
            <div style="display:flex;align-items:center;gap:8px">
              <span style="font-size:22px">🛍️</span>
              <h2>${t('shop.title')}</h2>
            </div>
            <button class="modal-close" id="shop-close">✕</button>
          </div>
          <p style="margin:0 16px 4px;font-size:13px;color:var(--text-muted)">${t('shop.subtitle')}</p>
          <div class="modal-body prize-grid">${prizesHtml}</div>
        </div>
      </div>`;
    document.body.appendChild(el.firstElementChild);

    document.getElementById('shop-close').addEventListener('click', closeShop);
    document.getElementById('shop-backdrop').addEventListener('click', closeShop);
    document.getElementById('shop-modal').addEventListener('click', e => {
      if (e.target.dataset.exchange) {
        const tg = window.Telegram?.WebApp;
        if (tg?.showAlert) tg.showAlert(t('shop.unavailable'));
        else alert(t('shop.unavailable'));
      }
    });
  }

  function openShop() {
    ensureShopModal();
    document.getElementById('shop-modal').classList.remove('hidden');
    document.querySelector('.app').style.overflow = 'hidden';
  }

  function closeShop() {
    document.getElementById('shop-modal').classList.add('hidden');
    document.querySelector('.app').style.overflow = '';
  }

  // ── History Modal ──────────────────────────────────────────────────────────

  function openHistoryModal(history) {
    const t = i18n.t.bind(i18n);
    let modal = document.getElementById('history-modal');
    if (!modal) {
      const el = document.createElement('div');
      el.innerHTML = `
        <div id="history-modal" class="modal hidden">
          <div class="modal-backdrop" id="history-backdrop"></div>
          <div class="modal-sheet">
            <div class="modal-header">
              <div style="display:flex;align-items:center;gap:8px">
                <span style="font-size:22px">📋</span>
                <h2>${t('profile.historyBtn')}</h2>
              </div>
              <button class="modal-close" id="history-close">✕</button>
            </div>
            <div class="modal-body" id="history-modal-body"></div>
          </div>
        </div>`;
      document.body.appendChild(el.firstElementChild);
      modal = document.getElementById('history-modal');
      document.getElementById('history-close').addEventListener('click', closeHistoryModal);
      document.getElementById('history-backdrop').addEventListener('click', closeHistoryModal);
    }

    const body = document.getElementById('history-modal-body');
    if (history.length > 0) {
      body.innerHTML = history.map(h => {
        const pts = h.pointsAwarded;
        const cls = pts !== null && pts !== undefined ? ptsCls(pts) : '';
        const ptsDisp = pts !== null && pts !== undefined ? `+${pts}` : '–';
        const score = h.actualHome !== null
          ? `${h.predictedHome}–${h.predictedAway} / ${h.actualHome}–${h.actualAway}`
          : `${h.predictedHome}–${h.predictedAway}`;
        return `<div class="history-item">
          <div class="history-match">${escHtml(h.homeTeam)} – ${escHtml(h.awayTeam)}</div>
          <div class="history-scores">${score}</div>
          <div class="history-pts ${cls}">${ptsDisp}</div>
        </div>`;
      }).join('');
    } else {
      body.innerHTML = `<div class="empty-state">
        <div class="empty-icon">🔮</div>
        <div class="empty-sub">${t('profile.noHistory')}</div>
      </div>`;
    }

    modal.classList.remove('hidden');
    document.querySelector('.app').style.overflow = 'hidden';
  }

  function closeHistoryModal() {
    document.getElementById('history-modal').classList.add('hidden');
    document.querySelector('.app').style.overflow = '';
  }

  // ── Active Predictions Modal ───────────────────────────────────────────────

  function openActiveModal(active) {
    const t = i18n.t.bind(i18n);
    let modal = document.getElementById('active-modal');
    if (!modal) {
      const el = document.createElement('div');
      el.innerHTML = `
        <div id="active-modal" class="modal hidden">
          <div class="modal-backdrop" id="active-backdrop"></div>
          <div class="modal-sheet">
            <div class="modal-header">
              <div style="display:flex;align-items:center;gap:8px">
                <span style="font-size:22px">⏳</span>
                <h2>${t('profile.activeBtn')}</h2>
              </div>
              <button class="modal-close" id="active-close">✕</button>
            </div>
            <div class="modal-body" id="active-modal-body"></div>
          </div>
        </div>`;
      document.body.appendChild(el.firstElementChild);
      modal = document.getElementById('active-modal');
      document.getElementById('active-close').addEventListener('click', closeActiveModal);
      document.getElementById('active-backdrop').addEventListener('click', closeActiveModal);
    }

    const body = document.getElementById('active-modal-body');
    if (active.length > 0) {
      body.innerHTML = active.map(p => {
        const isLive = p.matchStatus === 'live';
        const matchDateStr = new Date(p.matchDate).toLocaleDateString(
          i18n.lang === 'ru' ? 'ru-RU' : 'en-GB',
          { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }
        );
        const liveBadge = isLive ? `<span class="live-badge">LIVE</span>` : '';
        return `<div class="active-pred-item">
          <div class="active-pred-match">${escHtml(p.homeTeam)} – ${escHtml(p.awayTeam)}${liveBadge}</div>
          <div class="active-pred-score">${p.predictedHome}–${p.predictedAway}</div>
          <div class="active-pred-meta">${matchDateStr}</div>
        </div>`;
      }).join('');
    } else {
      body.innerHTML = `<div class="empty-state">
        <div class="empty-icon">⏳</div>
        <div class="empty-sub">${t('profile.noActive')}</div>
      </div>`;
    }

    modal.classList.remove('hidden');
    document.querySelector('.app').style.overflow = 'hidden';
  }

  function closeActiveModal() {
    document.getElementById('active-modal').classList.add('hidden');
    document.querySelector('.app').style.overflow = '';
  }

  // ── Friend Requests Modal ──────────────────────────────────────────────────

  function openFriendRequestsModal() {
    const t = i18n.t.bind(i18n);
    let modal = document.getElementById('freq-modal');
    if (!modal) {
      const el = document.createElement('div');
      el.innerHTML = `
        <div id="freq-modal" class="modal hidden">
          <div class="modal-backdrop" id="freq-backdrop"></div>
          <div class="modal-sheet">
            <div class="modal-header">
              <div style="display:flex;align-items:center;gap:8px">
                <span style="font-size:22px">🔔</span>
                <h2>${t('friends.requests')}</h2>
              </div>
              <button class="modal-close" id="freq-close">✕</button>
            </div>
            <div class="modal-body" id="freq-modal-body"><div class="spinner"></div></div>
          </div>
        </div>`;
      document.body.appendChild(el.firstElementChild);
      modal = document.getElementById('freq-modal');
      document.getElementById('freq-close').addEventListener('click', closeFriendRequestsModal);
      document.getElementById('freq-backdrop').addEventListener('click', closeFriendRequestsModal);
    }

    modal.classList.remove('hidden');
    document.querySelector('.app').style.overflow = 'hidden';
    loadFriendRequestsIntoModal();
  }

  async function loadFriendRequestsIntoModal() {
    const t = i18n.t.bind(i18n);
    const body = document.getElementById('freq-modal-body');
    if (!body) return;
    body.innerHTML = '<div class="spinner"></div>';
    try {
      const requests = await Api.getFriendReqs();
      renderFriendRequestsList(requests);
      updateBellBadge(requests.length);
    } catch(_) {
      body.innerHTML = `<div class="empty-state"><div class="empty-icon">⚠️</div></div>`;
    }
  }

  function renderFriendRequestsList(requests) {
    const t = i18n.t.bind(i18n);
    const body = document.getElementById('freq-modal-body');
    if (!body) return;

    if (requests.length === 0) {
      body.innerHTML = `<div class="empty-state">
        <div class="empty-icon">🔔</div>
        <div class="empty-sub">${t('friends.noRequests')}</div>
      </div>`;
      return;
    }

    body.innerHTML = requests.map(r => `
      <div class="friend-req-item" data-req-id="${r.id}">
        ${friendAvatarHtml(r)}
        <div class="friend-req-info">
          <div class="friend-req-name">${escHtml(r.firstName)}</div>
          ${r.username ? `<div class="friend-req-nick">@${escHtml(r.username)}</div>` : ''}
        </div>
        <div class="friend-req-actions">
          <button class="btn-accept" data-accept="${r.id}" title="Принять">✓</button>
          <button class="btn-decline" data-decline="${r.id}" title="Отклонить">✕</button>
        </div>
      </div>`).join('');

    body.addEventListener('click', async e => {
      const acceptId = e.target.dataset.accept;
      const declineId = e.target.dataset.decline;
      if (!acceptId && !declineId) return;

      const id = parseInt(acceptId || declineId, 10);
      e.target.disabled = true;

      try {
        if (acceptId) {
          await Api.acceptFriend(id);
        } else {
          await Api.declineFriend(id);
        }
        // Remove item from list
        const item = body.querySelector(`[data-req-id="${id}"]`);
        if (item) item.remove();
        // Re-check if empty
        if (body.querySelectorAll('.friend-req-item').length === 0) {
          body.innerHTML = `<div class="empty-state">
            <div class="empty-icon">🔔</div>
            <div class="empty-sub">${t('friends.noRequests')}</div>
          </div>`;
        }
        updateBellBadge(body.querySelectorAll('.friend-req-item').length);
        // Refresh friends count in profile
        if (_data) {
          if (acceptId) _data.friendsCount = (_data.friendsCount || 0) + 1;
          const countEl = document.getElementById('friends-count-val');
          if (countEl) countEl.textContent = _data.friendsCount || 0;
        }
      } catch(_) {
        e.target.disabled = false;
      }
    }, { once: false });
  }

  function closeFriendRequestsModal() {
    const modal = document.getElementById('freq-modal');
    if (modal) modal.classList.add('hidden');
    document.querySelector('.app').style.overflow = '';
  }

  function updateBellBadge(count) {
    const badge = document.getElementById('bell-badge');
    if (!badge) return;
    if (count > 0) {
      badge.textContent = count;
      badge.classList.remove('hidden');
    } else {
      badge.classList.add('hidden');
    }
  }

  // ── Friends List Modal ─────────────────────────────────────────────────────

  function openFriendsListModal() {
    const t = i18n.t.bind(i18n);
    let modal = document.getElementById('flist-modal');
    if (!modal) {
      const el = document.createElement('div');
      el.innerHTML = `
        <div id="flist-modal" class="modal hidden">
          <div class="modal-backdrop" id="flist-backdrop"></div>
          <div class="modal-sheet">
            <div class="modal-header">
              <div style="display:flex;align-items:center;gap:8px">
                <span style="font-size:22px">👥</span>
                <h2>${t('friends.list')}</h2>
              </div>
              <button class="modal-close" id="flist-close">✕</button>
            </div>
            <div class="friend-search">
              <input class="friend-search-input" id="friend-search-input"
                placeholder="${t('friends.searchPlaceholder')}" maxlength="32" />
              <button class="friend-search-btn" id="friend-search-btn">${t('friends.addBtn')}</button>
            </div>
            <div id="friend-search-msg" class="friend-search-msg" style="display:none"></div>
            <div class="modal-body" id="flist-modal-body"><div class="spinner"></div></div>
          </div>
        </div>`;
      document.body.appendChild(el.firstElementChild);
      modal = document.getElementById('flist-modal');
      document.getElementById('flist-close').addEventListener('click', closeFriendsListModal);
      document.getElementById('flist-backdrop').addEventListener('click', closeFriendsListModal);

      const searchBtn = document.getElementById('friend-search-btn');
      const searchInput = document.getElementById('friend-search-input');

      const doSearch = async () => {
        const username = searchInput.value.trim();
        if (!username) return;
        searchBtn.disabled = true;
        const msgEl = document.getElementById('friend-search-msg');
        msgEl.style.display = 'none';
        try {
          await Api.sendFriendReq(username);
          msgEl.textContent = t('friends.sent');
          msgEl.className = 'friend-search-msg success';
          msgEl.style.display = 'block';
          searchInput.value = '';
        } catch(err) {
          const serverErr = err.data?.error || '';
          let msg = t('friends.notFound');
          if (serverErr.includes('друзьях') || serverErr.includes('друзьях'))
            msg = t('friends.alreadyFriends');
          else if (serverErr.includes('отправлена'))
            msg = t('friends.alreadySent');
          msgEl.textContent = msg;
          msgEl.className = 'friend-search-msg error';
          msgEl.style.display = 'block';
        } finally {
          searchBtn.disabled = false;
        }
      };

      searchBtn.addEventListener('click', doSearch);
      searchInput.addEventListener('keydown', e => { if (e.key === 'Enter') doSearch(); });
    }

    modal.classList.remove('hidden');
    document.querySelector('.app').style.overflow = 'hidden';
    loadFriendsIntoModal();
  }

  async function loadFriendsIntoModal() {
    const t = i18n.t.bind(i18n);
    const body = document.getElementById('flist-modal-body');
    if (!body) return;
    body.innerHTML = '<div class="spinner"></div>';
    try {
      const friends = await Api.getFriends();
      if (friends.length === 0) {
        body.innerHTML = `<div class="empty-state">
          <div class="empty-icon">👥</div>
          <div class="empty-sub">${t('profile.noFriends')}</div>
        </div>`;
      } else {
        body.innerHTML = friends.map(f => `
          <div class="friend-item">
            ${friendAvatarHtml(f)}
            <div class="friend-item-info">
              <div class="friend-item-name">${escHtml(f.firstName)}</div>
              ${f.username ? `<div class="friend-item-nick">@${escHtml(f.username)}</div>` : ''}
            </div>
          </div>`).join('');
      }
    } catch(_) {
      body.innerHTML = `<div class="empty-state"><div class="empty-icon">⚠️</div></div>`;
    }
  }

  function closeFriendsListModal() {
    const modal = document.getElementById('flist-modal');
    if (modal) modal.classList.add('hidden');
    document.querySelector('.app').style.overflow = '';
  }

  // ── Helpers ────────────────────────────────────────────────────────────────

  function friendAvatarHtml(user) {
    if (user.avatarUrl)
      return `<img class="friend-avatar" src="${user.avatarUrl}"
               alt="${escHtml(user.firstName)}"
               onerror="this.outerHTML='${friendAvatarPhHtml(user)}'">`;
    return friendAvatarPhHtml(user);
  }

  function friendAvatarPhHtml(user) {
    const initial = (user.firstName || '?').charAt(0).toUpperCase();
    return `<div class="friend-avatar-ph">${escHtml(initial)}</div>`;
  }

  function avatarHtml(user) {
    if (user.avatarUrl)
      return `<img class="profile-avatar" src="${user.avatarUrl}"
               alt="${escHtml(user.firstName)}"
               onerror="this.outerHTML='${initialsHtml(user)}'">`;
    return initialsHtml(user);
  }

  function initialsHtml(user) {
    const initials = (user.firstName || '?').charAt(0).toUpperCase() +
                     ((user.firstName || '').split(' ')[1] || '').charAt(0).toUpperCase();
    return `<div class="profile-avatar-ph">${escHtml(initials)}</div>`;
  }

  function ptsCls(pts) {
    if (pts >= 3) return 'exact';
    if (pts === 1) return 'outcome';
    return 'miss';
  }

  function fmtDate(iso) {
    return new Date(iso).toLocaleDateString(
      i18n.lang === 'ru' ? 'ru-RU' : 'en-GB',
      { day: 'numeric', month: 'long', year: 'numeric' }
    );
  }

  async function loadFriendRequestsCount() {
    try {
      const requests = await Api.getFriendReqs();
      updateBellBadge(requests.length);
    } catch(_) {}
  }

  // ── Render ─────────────────────────────────────────────────────────────────

  function render(data) {
    const container = document.getElementById('profile-body');
    const s = data.stats;
    const t = i18n.t.bind(i18n);

    // Header (bell only on own profile)
    const bellHtml = _isOwnProfile ? `
      <div class="bell-btn-wrap" id="bell-btn-wrap">
        <button class="bell-btn" id="bell-btn">🔔</button>
        <span class="bell-badge hidden" id="bell-badge">0</span>
      </div>` : '';

    let html = `
    <div class="profile-header">
      ${avatarHtml(data)}
      <div class="profile-info">
        <div class="profile-name">${escHtml(data.firstName)}</div>
        ${data.username ? `<div class="profile-username">@${escHtml(data.username)}</div>` : ''}
        <div class="profile-since">${t('profile.since')} ${fmtDate(data.registeredAt)}</div>
      </div>
      ${bellHtml}
    </div>`;

    // Stats grid
    html += `
    <div class="stats-grid">
      <div class="stat-cell">
        <div class="stat-value">${s.totalPoints}</div>
        <div class="stat-label">${t('profile.points')}</div>
      </div>
      <div class="stat-cell">
        <div class="stat-value">${s.outcomeRate}%</div>
        <div class="stat-label">${t('profile.correct')}</div>
      </div>
      <div class="stat-cell">
        <div class="stat-value">${s.exactRate}%</div>
        <div class="stat-label">${t('profile.exact')}</div>
      </div>
    </div>`;

    // Friends stat (only on own profile)
    if (_isOwnProfile) {
      html += `
      <div class="friends-stat-row" id="friends-stat-btn">
        <span class="friends-stat-count" id="friends-count-val">${data.friendsCount || 0}</span>
        <span class="friends-stat-label">👥 ${t('profile.friends')}</span>
      </div>`;
    }

    // Shop button
    html += `<button class="shop-btn" id="open-shop-btn">🛍️ ${t('shop.btn')}</button>`;

    // Prediction buttons
    html += `<div class="pred-btns-row">
      <button class="pred-action-btn" id="open-history-btn">${t('profile.historyBtn')}</button>
      <button class="pred-action-btn" id="open-active-btn">${t('profile.activeBtn')}</button>
    </div>`;

    // Favourite team
    if (data.favoriteTeam) {
      html += `<div class="section-title">${t('profile.fav')}</div>
      <div class="favorite-team-row">
        ${data.favoriteTeam.emblemUrl
          ? `<img class="fav-emblem" src="${data.favoriteTeam.emblemUrl}" alt="${escHtml(data.favoriteTeam.name)}">`
          : ''}
        <div class="fav-name">${escHtml(data.favoriteTeam.name)}</div>
      </div>`;
    }

    // Achievements
    html += `<div class="section-title">${t('profile.achievements')}</div>`;
    if (data.achievements && data.achievements.length > 0) {
      const achHtml = data.achievements.map(a => {
        const name = i18n.lang === 'ru' ? a.nameRu : a.nameEn;
        const desc = i18n.lang === 'ru' ? a.descriptionRu : a.descriptionEn;
        return `<div class="achievement-chip" title="${escHtml(desc)}">
          <span class="ach-icon">${a.icon}</span>
          <span class="ach-name">${escHtml(name)}</span>
        </div>`;
      }).join('');
      html += `<div class="achievement-grid">${achHtml}</div>`;
    } else {
      html += `<div class="empty-state" style="padding:20px">
        <div class="empty-icon">🏅</div>
        <div class="empty-sub">${t('profile.noAchievements')}</div>
      </div>`;
    }

    container.innerHTML = html;

    document.getElementById('open-shop-btn')?.addEventListener('click', openShop);
    document.getElementById('open-history-btn')?.addEventListener('click', () => openHistoryModal(data.history || []));
    document.getElementById('open-active-btn')?.addEventListener('click', () => openActiveModal(data.activePredictions || []));

    if (_isOwnProfile) {
      document.getElementById('bell-btn')?.addEventListener('click', openFriendRequestsModal);
      document.getElementById('friends-stat-btn')?.addEventListener('click', openFriendsListModal);
    }
  }

  // ── Load ───────────────────────────────────────────────────────────────────

  async function load(telegramId) {
    _isOwnProfile = !telegramId;
    const container = document.getElementById('profile-body');
    container.innerHTML = '<div class="spinner"></div>';
    try {
      _data = telegramId ? await Api.user(telegramId) : await Api.me();
      render(_data);
      if (_isOwnProfile) {
        loadFriendRequestsCount();
      }
    } catch(e) {
      const msg = e.data?.error || e.message || 'Failed to load profile';
      container.innerHTML = `<div class="empty-state">
        <div class="empty-icon">⚠️</div>
        <div class="empty-msg">${msg}</div>
      </div>`;
    }
  }

  function invalidate() { _data = null; }

  return { load, invalidate };
})();
