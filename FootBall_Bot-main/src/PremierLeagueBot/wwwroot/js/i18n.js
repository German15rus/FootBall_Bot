const TRANSLATIONS = {
  en: {
    'nav.standings':   'Table',
    'nav.matches':     'Matches',
    'nav.predictions': 'Predict',
    'nav.profile':     'Profile',
    'standings.title': 'Premier League',
    'matches.title':   'Upcoming Matches',
    'matches.subtitle':'Next 7 days',
    'predictions.title':   'Predictions',
    'predictions.subtitle':'Next 7 days',
    'predictions.coeffBanner': 'Dynamic scoring: some matches award up to 5 pts for exact scores!',
    'team.squad':  'Squad',
    'team.recent': 'Last 5',
    'col.pos': 'POS', 'col.club': 'CLUB', 'col.p': 'P', 'col.w': 'W',
    'col.d': 'D', 'col.l': 'L', 'col.gd': 'GD', 'col.pts': 'PTS',
    'pred.deadline': 'Deadline: {d}',
    'pred.closed':   'Predictions closed',
    'pred.save':     'SAVE PREDICTION',
    'pred.saved':    'SAVED ✓',
    'pred.noMatches':'No upcoming matches',
    'pred.exact':    'Exact score!',
    'pred.outcome':  'Correct outcome',
    'pred.miss':     'Miss',
    'profile.points':  'Points',
    'profile.correct': 'Correct',
    'profile.exact':   'Exact',
    'profile.since':   'Member since',
    'profile.fav':     'Favourite Team',
    'profile.achievements': 'Achievements',
    'profile.history': 'Prediction History',
    'profile.noHistory': 'No predictions yet',
    'profile.noAchievements': 'No achievements yet',
    'profile.you': 'my',
    'profile.actual': 'actual',
  },
  ru: {
    'nav.standings':   'Таблица',
    'nav.matches':     'Матчи',
    'nav.predictions': 'Предикты',
    'nav.profile':     'Профиль',
    'standings.title': 'Премьер-Лига',
    'matches.title':   'Ближайшие матчи',
    'matches.subtitle':'7 дней',
    'predictions.title':   'Предикты',
    'predictions.subtitle':'7 дней',
    'predictions.coeffBanner': 'Динамические очки: за некоторые точные счёта можно получить до 5 баллов!',
    'team.squad':  'Состав',
    'team.recent': 'Последние 5',
    'col.pos': 'М', 'col.club': 'КЛУБ', 'col.p': 'И', 'col.w': 'В',
    'col.d': 'Н', 'col.l': 'П', 'col.gd': 'ГР', 'col.pts': 'О',
    'pred.deadline': 'До: {d}',
    'pred.closed':   'Приём закрыт',
    'pred.save':     'СОХРАНИТЬ',
    'pred.saved':    'СОХРАНЕНО ✓',
    'pred.noMatches':'Матчей нет',
    'pred.exact':    'Точный счёт!',
    'pred.outcome':  'Исход угадан',
    'pred.miss':     'Мимо',
    'profile.points':  'Очки',
    'profile.correct': 'Угадано',
    'profile.exact':   'Точных',
    'profile.since':   'В игре с',
    'profile.fav':     'Любимая команда',
    'profile.achievements': 'Достижения',
    'profile.history': 'История предиктов',
    'profile.noHistory': 'Пока нет предиктов',
    'profile.noAchievements': 'Пока нет достижений',
    'profile.you': 'мой',
    'profile.actual': 'факт',
  }
};

const i18n = (() => {
  let lang = 'ru';

  function init() {
    const tgLang = window.Telegram?.WebApp?.initDataUnsafe?.user?.language_code;
    lang = (tgLang && tgLang.startsWith('ru')) ? 'ru' : 'en';
    applyAll();
  }

  function t(key, vars = {}) {
    let s = (TRANSLATIONS[lang] || TRANSLATIONS.en)[key] || key;
    for (const [k, v] of Object.entries(vars)) s = s.replace(`{${k}}`, v);
    return s;
  }

  function applyAll() {
    document.querySelectorAll('[data-i18n]').forEach(el => {
      el.textContent = t(el.dataset.i18n);
    });
  }

  return { init, t, get lang() { return lang; } };
})();
