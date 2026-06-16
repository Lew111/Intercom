#pragma once

#ifndef CHATITEMDELEGATE_H
#define CHATITEMDELEGATE_H

#include <QStyledItemDelegate>
#include <QPainter>
#include <QApplication>
#include "chatlistmodel.h"

class ChatItemDelegate : public QStyledItemDelegate
{
    Q_OBJECT
public:
    explicit ChatItemDelegate(QObject* parent = nullptr);

    void paint(QPainter* painter, const QStyleOptionViewItem& option,
               const QModelIndex& index) const override;
    QSize sizeHint(const QStyleOptionViewItem& option,
                   const QModelIndex& index) const override;

private:
    QColor getAvatarColor(const QString& text) const;
};

#endif 
