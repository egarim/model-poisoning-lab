"""Crisis pre-filter. Runs BEFORE the model, so safety never depends on the model.

This is a first-pass keyword filter for testing. Before launch, replace or
supplement it with a proper classifier and have the hotline numbers verified
by someone local to each country.
"""
import re

CRISIS_PATTERNS = {
    "es": [
        r"no quiero (seguir )?viv(ir|iendo)", r"ya no quiero (estar aqu[ií]|existir)", r"quiero morir(me)?", r"matarme", r"suicid",
        r"quitarme la vida", r"hacerme da[ñn]o", r"cortarme", r"no vale la pena vivir",
    ],
    "pt": [
        r"n[ãa]o quero (mais )?(viver|continuar vivendo|existir)", r"quero morrer", r"me matar", r"suic[ií]d",
        r"tirar (a )?minha (pr[óo]pria )?vida", r"me machucar", r"me cortar",
    ],
    "ru": [
        r"не хочу (больше )?жить", r"хочу умереть", r"покончить с собой", r"суицид",
        r"убить себя", r"порезать себя", r"навредить себе", r"нет смысла жить",
    ],
}

CRISIS_REPLIES = {
    "es": (
        "Te escucho, y lo que sientes importa. Ahora mismo no quiero hablarte de cartas: "
        "quiero que estés a salvo. Por favor llama a la Línea de la Vida, 800 911 2000 "
        "(gratis, 24 horas), o al 911 si estás en peligro. Y si puedes, escríbele ahora "
        "a alguien de confianza. Aquí sigo contigo."
    ),
    "pt": (
        "Eu te escuto, e o que você sente importa. Agora não quero falar de cartas: "
        "quero que você esteja seguro. Por favor ligue para o CVV no 188 (gratuito, 24h), "
        "ou 192 se estiver em perigo. E se puder, chama agora alguém de confiança. "
        "Eu continuo aqui com você."
    ),
    "ru": (
        "Я тебя слышу, и то, что ты чувствуешь, важно. Сейчас не время для карт — "
        "мне важно, чтобы ты была в безопасности. Пожалуйста, позвони на бесплатную "
        "линию помощи 8-800-2000-122 или в 112, если ты в опасности. И если можешь, "
        "напиши сейчас кому-то, кому доверяешь. Я рядом."
    ),
}


def check_crisis(text: str, lang: str):
    """Return a fixed crisis reply if the message matches, else None."""
    lowered = text.lower()
    for pattern in CRISIS_PATTERNS.get(lang, []):
        if re.search(pattern, lowered):
            return CRISIS_REPLIES[lang]
    return None
