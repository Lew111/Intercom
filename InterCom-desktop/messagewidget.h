#pragma once

#ifndef MESSAGEWIDGET_H
#define MESSAGEWIDGET_H
#include <QWidget>
#include <QLabel>
#include <QDateTime>
#include <QTextEdit>

class MessageWidget : public QWidget
{
    Q_OBJECT
public:
    MessageWidget(const QString& text, bool isMine, const QDateTime& time, QWidget* parent = nullptr);

    void setMessageStyle(const QString& style);
    void updateTheme();

protected:
    void resizeEvent(QResizeEvent* event) override;

private:
    void updateBubbleSize();
    void applyStyle();  

    QLabel* textLabel;
    QLabel* timeLabel;
    QWidget* bubble;
    QString messageText;
    bool isMine;
    QString m_currentStyle;  
};

#endif 