package com.bibliotecaar;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.speech.tts.TextToSpeech;
import android.speech.tts.UtteranceProgressListener;
import android.speech.tts.Voice;

import java.text.BreakIterator;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/** Reads chapter text with the best installed Spanish voice available on the device. */
public final class NarrationTts extends UtteranceProgressListener implements TextToSpeech.OnInitListener {
    private static final Locale SPANISH_PERU = new Locale("es", "PE");
    private static final int MAX_CHUNK_LENGTH = 280;

    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final List<String> chunks = new ArrayList<>();
    private TextToSpeech engine;
    private Voice offlineVoice;
    private boolean networkVoice;
    private boolean triedOfflineFallback;
    private boolean ready;
    private boolean speaking;
    private boolean paused;
    private boolean closed;
    private String error = "";
    private String activeUtteranceId;
    private int chunkIndex;
    private long utteranceCounter;

    public NarrationTts(Context context) {
        engine = new TextToSpeech(context.getApplicationContext(), this);
    }

    @Override
    public void onInit(int status) {
        mainHandler.post(() -> {
            synchronized (NarrationTts.this) {
                if (closed)
                    return;
                if (status != TextToSpeech.SUCCESS || engine == null) {
                    error = "No se pudo iniciar la voz del teléfono.";
                    return;
                }
                engine.setOnUtteranceProgressListener(this);
                configureSpanishVoice();
            }
        });
    }

    private void configureSpanishVoice() {
        Voice bestOffline = null;
        Voice bestNetwork = null;
        Set<Voice> voices = engine.getVoices();
        if (voices != null) {
            for (Voice voice : voices) {
                if (voice == null || !"es".equals(voice.getLocale().getLanguage()))
                    continue;
                Set<String> features = voice.getFeatures();
                if (features != null && features.contains(TextToSpeech.Engine.KEY_FEATURE_NOT_INSTALLED))
                    continue;
                if (voice.isNetworkConnectionRequired()) {
                    if (bestNetwork == null || score(voice) > score(bestNetwork))
                        bestNetwork = voice;
                } else if (bestOffline == null || score(voice) > score(bestOffline)) {
                    bestOffline = voice;
                }
            }
        }

        offlineVoice = bestOffline;
        Voice preferred = bestOffline;
        if ((bestOffline == null || bestOffline.getQuality() < Voice.QUALITY_HIGH) &&
            bestNetwork != null && (preferred == null || score(bestNetwork) > score(preferred))) {
            preferred = bestNetwork;
        }
        if (preferred != null && engine.setVoice(preferred) == TextToSpeech.SUCCESS) {
            networkVoice = preferred.isNetworkConnectionRequired();
            ready = true;
        } else {
            int languageResult = engine.setLanguage(SPANISH_PERU);
            ready = languageResult >= TextToSpeech.LANG_AVAILABLE;
        }

        if (ready) {
            engine.setSpeechRate(0.94f);
            engine.setPitch(1.0f);
            error = "";
        } else {
            error = "Instala o activa una voz en español en los ajustes de voz del teléfono.";
        }
    }

    private static int score(Voice voice) {
        String country = voice.getLocale().getCountry();
        int region = "PE".equals(country) ? 30 : "MX".equals(country) ? 20 : "ES".equals(country) ? 10 : 0;
        return voice.getQuality() * 10 + region;
    }

    public synchronized boolean isReady() { return ready && !closed; }
    public synchronized boolean isSpeaking() { return speaking && !closed; }
    public synchronized boolean isPaused() { return paused && !closed; }
    public synchronized String getError() { return error; }

    public synchronized boolean play(String text) {
        if (!isReady() || text == null || text.trim().isEmpty())
            return false;
        stopInternal();
        splitIntoChunks(text);
        if (chunks.isEmpty())
            return false;
        error = "";
        triedOfflineFallback = false;
        speaking = true;
        speakCurrentChunk();
        return speaking;
    }

    public synchronized boolean resume() {
        if (!isReady() || !paused || chunkIndex >= chunks.size())
            return false;
        paused = false;
        speaking = true;
        speakCurrentChunk();
        return speaking;
    }

    public synchronized void pause() {
        if (!speaking)
            return;
        speaking = false;
        paused = true;
        activeUtteranceId = null;
        engine.stop();
    }

    public synchronized void stop() {
        stopInternal();
    }

    public synchronized void dispose() {
        if (closed)
            return;
        closed = true;
        stopInternal();
        if (engine != null) {
            engine.shutdown();
            engine = null;
        }
    }

    private void stopInternal() {
        activeUtteranceId = null;
        speaking = false;
        paused = false;
        chunkIndex = 0;
        chunks.clear();
        if (engine != null)
            engine.stop();
    }

    private void speakCurrentChunk() {
        if (closed || engine == null || chunkIndex >= chunks.size()) {
            speaking = false;
            paused = false;
            activeUtteranceId = null;
            return;
        }
        activeUtteranceId = "chapter-" + (++utteranceCounter);
        int result = engine.speak(chunks.get(chunkIndex), TextToSpeech.QUEUE_FLUSH, null, activeUtteranceId);
        if (result != TextToSpeech.SUCCESS)
            handleSpeechError(activeUtteranceId);
    }

    @Override
    public void onStart(String utteranceId) { }

    @Override
    public void onDone(String utteranceId) {
        mainHandler.post(() -> {
            synchronized (NarrationTts.this) {
                if (!closed && speaking && utteranceId.equals(activeUtteranceId)) {
                    chunkIndex++;
                    speakCurrentChunk();
                }
            }
        });
    }

    @Override
    public void onError(String utteranceId) {
        mainHandler.post(() -> {
            synchronized (NarrationTts.this) {
                handleSpeechError(utteranceId);
            }
        });
    }

    @Override
    public void onError(String utteranceId, int errorCode) {
        onError(utteranceId);
    }

    private void handleSpeechError(String utteranceId) {
        if (closed || !speaking || !utteranceId.equals(activeUtteranceId))
            return;
        if (networkVoice && offlineVoice != null && !triedOfflineFallback &&
            engine.setVoice(offlineVoice) == TextToSpeech.SUCCESS) {
            networkVoice = false;
            triedOfflineFallback = true;
            speakCurrentChunk();
            return;
        }
        speaking = false;
        paused = false;
        activeUtteranceId = null;
        error = "La voz no pudo leer el capítulo. Comprueba el audio del teléfono e inténtalo otra vez.";
    }

    private void splitIntoChunks(String text) {
        String normalized = text.trim().replaceAll("\\s+", " ");
        BreakIterator sentences = BreakIterator.getSentenceInstance(SPANISH_PERU);
        sentences.setText(normalized);
        for (int start = sentences.first(), end = sentences.next(); end != BreakIterator.DONE;
             start = end, end = sentences.next()) {
            String sentence = normalized.substring(start, end).trim();
            while (sentence.length() > MAX_CHUNK_LENGTH) {
                int split = sentence.lastIndexOf(' ', MAX_CHUNK_LENGTH);
                if (split < MAX_CHUNK_LENGTH / 2)
                    split = MAX_CHUNK_LENGTH;
                chunks.add(sentence.substring(0, split).trim());
                sentence = sentence.substring(split).trim();
            }
            if (!sentence.isEmpty())
                chunks.add(sentence);
        }
    }
}
