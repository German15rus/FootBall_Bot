const ProfileTab = (() => {
  let _data = null;

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

    // History
    html += `<div class="section-title">${t('profile.history')}</div>`;
    if (data.history && data.history.length > 0) {
      html += data.history.map(h => {
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
      html += `<div class="empty-state" style="padding:20px">
        <div class="empty-icon">🔮</div>
        <div class="empty-sub">${t('profile.noHistory')}</div>
      </div>`;
    }

    container.innerHTML = html;
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
