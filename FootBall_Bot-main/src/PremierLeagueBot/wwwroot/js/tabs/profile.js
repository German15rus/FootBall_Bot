const ProfileTab = (() => {
  let _data = null;

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

  // ── Helpers ────────────────────────────────────────────────────────────────

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

  // ── Render ─────────────────────────────────────────────────────────────────

  function render(data) {
    const container = document.getElementById('profile-body');
    const s = data.stats;
    const t = i18n.t.bind(i18n);

    // Header
    let html = `
    <div class="profile-header">
      ${avatarHtml(data)}
      <div class="profile-info">
        <div class="profile-name">${escHtml(data.firstName)}</div>
        ${data.username ? `<div class="profile-username">@${escHtml(data.username)}</div>` : ''}
        <div class="profile-since">${t('profile.since')} ${fmtDate(data.registeredAt)}</div>
      </div>
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

    // Shop button
    html += `<button class="shop-btn" id="open-shop-btn">🛍️ ${t('shop.btn')}</button>`;

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

    // Prediction buttons
    html += `<div class="pred-btns-row">
      <button class="pred-action-btn" id="open-history-btn">${t('profile.historyBtn')}</button>
      <button class="pred-action-btn" id="open-active-btn">${t('profile.activeBtn')}</button>
    </div>`;

    container.innerHTML = html;

    document.getElementById('open-shop-btn')?.addEventListener('click', openShop);
    document.getElementById('open-history-btn')?.addEventListener('click', () => openHistoryModal(data.history || []));
    document.getElementById('open-active-btn')?.addEventListener('click', () => openActiveModal(data.activePredictions || []));
  }

  // ── Load ───────────────────────────────────────────────────────────────────

  async function load(telegramId) {
    const container = document.getElementById('profile-body');
    container.innerHTML = '<div class="spinner"></div>';
    try {
      _data = telegramId ? await Api.user(telegramId) : await Api.me();
      render(_data);
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
