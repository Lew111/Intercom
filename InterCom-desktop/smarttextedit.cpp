#include "smarttextedit.h"
#include <QScrollBar>
#include <QDebug>
#include <QKeyEvent>

SmartTextEdit::SmartTextEdit(QWidget* parent)
    : QTextEdit(parent)
{
    setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
    setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    setVerticalScrollBarPolicy(Qt::ScrollBarAsNeeded);
    setAcceptRichText(false);

    // Начальная высота для одной строки
    QFontMetrics fm(font());
    setFixedHeight(fm.height() + 10);

    // Обновляем высоту при изменении текста
    connect(this, &QTextEdit::textChanged, this, &SmartTextEdit::updateHeight);
}

void SmartTextEdit::setMaxVisibleLines(int lines)
{
    m_maxVisibleLines = qMax(1, lines);
    updateHeight();
}

void SmartTextEdit::keyPressEvent(QKeyEvent* event)
{
    // Enter (без Shift) - отправка сообщения
    if ((event->key() == Qt::Key_Return || event->key() == Qt::Key_Enter) &&
        event->modifiers() == Qt::NoModifier) {
        emit enterPressed(); // Сигнал для отправки сообщения
        return;
    }

    // Shift+Enter - новая строка
    if ((event->key() == Qt::Key_Return || event->key() == Qt::Key_Enter) &&
        (event->modifiers() & Qt::ShiftModifier)) {
        QTextEdit::keyPressEvent(event);
        updateHeight();
        return;
    }

    // Ctrl+Enter - тоже отправка (альтернатива)
    if ((event->key() == Qt::Key_Return || event->key() == Qt::Key_Enter) &&
        (event->modifiers() & Qt::ControlModifier)) {
        emit enterPressed();
        return;
    }

    QTextEdit::keyPressEvent(event);
    updateHeight();
}

void SmartTextEdit::resizeEvent(QResizeEvent* event)
{
    QTextEdit::resizeEvent(event);
    updateHeight();
}

void SmartTextEdit::updateHeight()
{
    QFontMetrics fm(font());
    int lineHeight = fm.lineSpacing();
    int documentHeight = document()->size().height();
    int lines = qCeil(documentHeight / lineHeight);

    if (lines <= 1) {
        // Одна строка
        setFixedHeight(lineHeight + 10);
        setVerticalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    } else if (lines <= m_maxVisibleLines) {
        // Несколько строк, но меньше максимума
        setFixedHeight(documentHeight + 10);
        setVerticalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    } else {
        // Больше максимума - включаем скролл
        setFixedHeight(lineHeight * m_maxVisibleLines + 10);
        setVerticalScrollBarPolicy(Qt::ScrollBarAsNeeded);
    }
}
