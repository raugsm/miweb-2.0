# Perception Engine

Purpose:

- convert Vision Engine output into objects;
- identify WhatsApp windows, chat list, active conversation, message bubbles, input box, payment proofs, audio and errors;
- combine local OCR, Windows accessibility and visual detection;
- emit `perception_event` contracts.

This engine must not leak browser cookies, tokens or hidden session data.

