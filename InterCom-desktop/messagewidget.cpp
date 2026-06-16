#include "messagewidget.h"
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QDateTime>
#include <QResizeEvent>

MessageWidget::MessageWidget(const QString& text, bool isMine, const QDateTime& time, QWidget* parent)
    : QWidget(parent), messageText(text), isMine(isMine), m_currentStyle("modern")
{
    textLabel = new QLabel(text);
    textLabel->setWordWrap(true);
    textLabel->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Preferred);
    textLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);

    timeLabel = new QLabel(time.toString("HH:mm"));

    bubble = new QWidget;
    bubble->setSizePolicy(QSizePolicy::Preferred, QSizePolicy::Preferred);

    auto v = new QVBoxLayout(bubble);
    v->setContentsMargins(8, 6, 8, 6);
    v->addWidget(textLabel);
    v->addWidget(timeLabel, 0, Qt::AlignRight);

    auto h = new QHBoxLayout(this);
    h->setContentsMargins(4, 2, 4, 2);
    h->addStretch(isMine ? 1 : 0);
    h->addWidget(bubble);
    h->addStretch(isMine ? 0 : 1);

    applyStyle();
    updateBubbleSize();
}

void MessageWidget::applyStyle()
{
    if (isMine) {
        if (m_currentStyle == "modern") {
            bubble->setStyleSheet("background:#4e9cff;color:white;border-radius:15px;padding:1px;");
            timeLabel->setStyleSheet("color: rgba(255,255,255,0.7); font-size: 10px;");
        } else if (m_currentStyle == "classic") {
            bubble->setStyleSheet("background:#0078fe;color:white;border-radius:8px;padding:1px;");
            timeLabel->setStyleSheet("color: #cccccc; font-size: 10px;");
        } else { 
            bubble->setStyleSheet("background:#4e9cff;color:white;border-radius:5px;padding:1px;");
            timeLabel->setStyleSheet("color: rgba(255,255,255,0.5); font-size: 9px;");
        }
    } else {
        if (m_currentStyle == "modern") {
            bubble->setStyleSheet("background:#e5e5e5;color:black;border-radius:15px;padding:1px;");
            timeLabel->setStyleSheet("color: gray; font-size: 10px;");
        } else if (m_currentStyle == "classic") {
            bubble->setStyleSheet("background:#e8e8e8;color:black;border-radius:8px;padding:1px;");
            timeLabel->setStyleSheet("color: #666666; font-size: 10px;");
        } else { 
            bubble->setStyleSheet("background:#f0f0f0;color:black;border-radius:5px;padding:1px;");
            timeLabel->setStyleSheet("color: #999999; font-size: 9px;");
        }
    }
}

void MessageWidget::setMessageStyle(const QString& style)
{
    m_currentStyle = style;
    applyStyle();
}

void MessageWidget::updateTheme()
{
    applyStyle();
}

void MessageWidget::resizeEvent(QResizeEvent* event)
{
    QWidget::resizeEvent(event);
    updateBubbleSize();
}

void MessageWidget::updateBubbleSize()
{
    if (!parentWidget()) return;

    int availableWidth = parentWidget()->width() * 0.7;

    QFontMetrics fm(textLabel->font());
    int textWidth = fm.horizontalAdvance(messageText);

    int padding = 40;
    int optimalWidth = qMin(textWidth + padding, availableWidth);

    bubble->setMinimumWidth(qMax(30, optimalWidth));
    bubble->setMaximumWidth(availableWidth);

    if (layout()) {
        layout()->invalidate();
        layout()->activate();
    }
}